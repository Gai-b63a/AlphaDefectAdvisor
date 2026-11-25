using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace Sts2Headless;

public sealed partial class RunSession
{
    // The retail selection screens keep their option lists private. Read the same models the UI owns.
    static T ReadSelectionField<T>(Type owner, object screen, string name)
    {
        var field = owner.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(screen) is T value ? value :
            throw new InvalidOperationException($"選択画面を読み込めません ({owner.Name}.{name})");
    }

    Decision? DiscoveryChoice(NChooseACardSelectionScreen screen)
    {
        var type = typeof(NChooseACardSelectionScreen);
        if (ReadSelectionField<bool>(type, screen, "_screenComplete") ||
            ReadSelectionField<bool>(type, screen, "_cardSelected") ||
            !ReadSelectionField<Control>(type, screen, "_cardRow").IsVisibleInTree()) return null;

        var options = ReadSelectionField<IReadOnlyList<CardModel>>(type, screen, "_cards");
        bool skip = ReadSelectionField<bool>(type, screen, "_canSkip") &&
            Visible<NChoiceSelectionSkipButton>(screen).Any(button => button.IsEnabled);
        return LiveCardChoices(options, skip ? 0 : 1, 1, false,
            new SelectInfo(null, null, "FromChooseACardScreen", null), null, false);
    }

    Decision? SmithChoice(NDeckUpgradeSelectScreen screen)
    {
        var type = typeof(NDeckUpgradeSelectScreen);
        var gridType = typeof(NCardGridSelectionScreen);
        if (!ReadSelectionField<NCardGrid>(gridType, screen, "_grid").IsVisibleInTree() ||
            ReadSelectionField<Control>(type, screen, "_upgradeSinglePreviewContainer").IsVisibleInTree() ||
            ReadSelectionField<Control>(type, screen, "_upgradeMultiPreviewContainer").IsVisibleInTree()) {
            WaitReason = "強化プレビューを確認中";
            return null;
        }
        var options = ReadSelectionField<IReadOnlyList<CardModel>>(gridType, screen, "_cards");
        var prefs = ReadSelectionField<CardSelectorPrefs>(type, screen, "_prefs");
        var selected = ReadSelectionField<HashSet<CardModel>>(type, screen, "_selectedCards");
        return LiveCardChoices(options.Where(c => c.IsUpgradable).ToArray(), prefs.MinSelect, prefs.MaxSelect,
            prefs.Cancelable, new SelectInfo(prefs.Prompt?.LocTable, prefs.Prompt?.LocEntryKey, "FromDeckForUpgrade", null),
            selected, true);
    }

    Decision? LiveCardChoices(IReadOnlyList<CardModel> cards, int min, int max, bool cancelable,
        SelectInfo info, IReadOnlySet<CardModel>? selected, bool upgrade)
    {
        if (cards.Count == 0 || max <= 0) return null;
        _selecting = new PendingCardChoice(cards, min, max, cancelable, info);
        var decision = new Decision(DecisionKind.CardSelect) { Selected = selected?.Count ?? 0 };
        for (int i = 0; i < cards.Count; i++) {
            var card = cards[i];
            if (selected?.Contains(card) == true) { _selected.Add(i); continue; }
            if (decision.Selected >= max) continue;
            decision.Add(ActionKind.SelectCard, upgrade ? $"{card.Title} を強化" : card.Title, () => {},
                a: card, b: upgrade ? Observation.UpgradedCopy(card) : null);
        }
        if (decision.Selected >= (upgrade ? Math.Max(1, min) : min))
            decision.Add(ActionKind.ConfirmSelection, upgrade ? "選択を確定" : "スキップ", () => {});
        return decision;
    }
}
