using static AlphaDefectAdvisor.AdvisorText;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
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

    static bool ReadyButton(NConfirmButton button) => button.IsVisibleInTree() && button.IsEnabled;

    Decision? GridChoice(NCardGridSelectionScreen screen)
    {
        var type = screen.GetType();
        var grid = ReadSelectionField<NCardGrid>(typeof(NCardGridSelectionScreen), screen, "_grid");
        var prefs = ReadSelectionField<CardSelectorPrefs>(type, screen, "_prefs");
        string method;
        EnchantmentModel? enchantment = null;
        switch (screen) {
            case NDeckUpgradeSelectScreen: method = "FromDeckForUpgrade"; break;
            case NDeckEnchantSelectScreen:
                method = "FromDeckForEnchantment";
                enchantment = ReadSelectionField<EnchantmentModel>(type, screen, "_enchantment");
                break;
            case NDeckTransformSelectScreen: method = "FromDeckForTransformation"; break;
            case NDeckCardSelectScreen: method = "FromDeckGeneric"; break;
            case NCombatPileCardSelectScreen: method = "FromCombatPile"; break;
            case NSimpleCardSelectScreen: method = "FromSimpleGrid"; break;
            default: WaitReason = T("unsupported", type.Name); return null;
        }

        // Preview confirmation applies to an already chosen card, and can cover a visible grid.
        bool Preview(string field) => ReadSelectionField<Control>(type, screen, field).IsVisibleInTree();
        bool preview = screen switch {
            NDeckUpgradeSelectScreen => Preview("_upgradeSinglePreviewContainer") || Preview("_upgradeMultiPreviewContainer"),
            NDeckEnchantSelectScreen => Preview("_enchantSinglePreviewContainer") || Preview("_enchantMultiPreviewContainer"),
            NDeckCardSelectScreen or NDeckTransformSelectScreen => Preview("_previewContainer"),
            _ => false
        };
        if (!grid.IsVisibleInTree() || preview) {
            WaitReason = T("cardPreview");
            return null;
        }

        // Use the grid's full backing list, including scrolled-off rows. The combat pile
        // screen updates this list when its pile or eligibility filter changes.
        var options = ReadSelectionField<IReadOnlyList<CardModel>>(typeof(NCardGrid), grid, "_cards");
        bool upgrade = screen is NDeckUpgradeSelectScreen;
        var cards = options.Where(c => (!upgrade || c.IsUpgradable) && (enchantment?.CanEnchant(c) ?? true)).ToArray();
        var selected = ReadSelectionField<HashSet<CardModel>>(type, screen, "_selectedCards");
        var info = new SelectInfo(prefs.Prompt?.LocTable, prefs.Prompt?.LocEntryKey, method, enchantment);
        bool canConfirm = !upgrade && ReadyButton(ReadSelectionField<NConfirmButton>(type, screen, "_confirmButton"));
        bool leave = prefs.Cancelable && Visible<NBackButton>(screen).Any(button => button.IsEnabled);
        return LiveCardChoices(cards, prefs.MinSelect, prefs.MaxSelect, leave, info, selected, upgrade, canConfirm);
    }

    Decision? HandChoice(NPlayerHand hand)
    {
        var type = typeof(NPlayerHand);
        if (ReadSelectionField<Control>(type, hand, "_upgradePreviewContainer").IsVisibleInTree()) {
            WaitReason = T("upgradePreview");
            return null;
        }
        var prefs = ReadSelectionField<CardSelectorPrefs>(type, hand, "_prefs");
        var selected = ReadSelectionField<List<CardModel>>(type, hand, "_selectedCards").ToHashSet();
        bool upgrade = hand.CurrentMode == NPlayerHand.Mode.UpgradeSelect;
        // Retail implements its selection predicate by hiding ineligible hand holders.
        var cards = hand.ActiveHolders.Where(h => h.IsVisibleInTree()).Select(h => h.CardModel)
            .OfType<CardModel>().Where(c => !upgrade || c.IsUpgradable).Union(selected).ToArray();
        var info = new SelectInfo(prefs.Prompt?.LocTable, prefs.Prompt?.LocEntryKey,
            upgrade ? "FromHandForUpgrade" : "FromHand", null);
        var confirm = ReadSelectionField<NConfirmButton>(type, hand, "_selectModeConfirmButton");
        return LiveCardChoices(cards, prefs.MinSelect, prefs.MaxSelect, false, info, selected, upgrade, ReadyButton(confirm));
    }

    Decision? BundleChoice(NChooseABundleSelectionScreen screen)
    {
        var type = typeof(NChooseABundleSelectionScreen);
        if (!ReadSelectionField<Control>(type, screen, "_bundleRow").IsVisibleInTree() ||
            ReadSelectionField<Control>(type, screen, "_bundlePreviewContainer").IsVisibleInTree()) {
            WaitReason = T("bundlePreview");
            return null;
        }
        var bundles = ReadSelectionField<IReadOnlyList<IReadOnlyList<CardModel>>>(type, screen, "_bundles");
        bool skip = ReadSelectionField<bool>(type, screen, "_canSkip") &&
            Visible<NChoiceSelectionSkipButton>(screen).Any(button => button.IsEnabled);
        return LiveBundleChoices(bundles, skip);
    }

    static Decision LiveBundleChoices(IReadOnlyList<IReadOnlyList<CardModel>> bundles, bool skip)
    {
        var decision = new Decision(DecisionKind.Bundle);
        for (int i = 0; i < bundles.Count; i++) {
            var cards = bundles[i];
            foreach (var card in cards) decision.Shown.Add((card, i == 0 ? Zone.Pack1 : Zone.Pack2, 0));
            decision.Add(ActionKind.PickBundle, string.Join(" + ", cards.Select(c => c.Title)), () => {},
                a: cards.FirstOrDefault(), f0: i);
        }
        if (skip)
            decision.Add(ActionKind.Leave, T("skip"), () => {});
        return decision;
    }

    Decision? LiveCardChoices(IReadOnlyList<CardModel> cards, int min, int max, bool cancelable,
        SelectInfo info, IReadOnlySet<CardModel>? selected, bool upgrade, bool canConfirm = true)
    {
        WaitReason = T("cardsWait");
        _selecting = new PendingCardChoice(cards, min, max, cancelable, info);
        var decision = new Decision(DecisionKind.CardSelect) { Selected = selected?.Count ?? 0 };
        for (int i = 0; i < cards.Count; i++) {
            var card = cards[i];
            if (selected?.Contains(card) == true) { _selected.Add(i); continue; }
            if (decision.Selected >= max) continue;
            decision.Add(ActionKind.SelectCard, upgrade ? T("upgrade", card.Title) : card.Title, () => {},
                a: card, b: upgrade ? Observation.UpgradedCopy(card) : null);
        }
        if (canConfirm && decision.Selected >= min)
            decision.Add(ActionKind.ConfirmSelection, decision.Selected > 0 ? T("confirm") : T("skip"), () => {});
        if (cancelable && decision.Selected == 0) decision.Add(ActionKind.Leave, T("cancel"), () => {});
        return decision;
    }
}
