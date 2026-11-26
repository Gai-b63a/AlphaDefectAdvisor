using static AlphaDefectAdvisor.AdvisorText;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Map;
using System.Reflection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace Sts2Headless;

public sealed partial class RunSession
{
    // No selector, bootstrap, Harmony patches or action execution in the retail game.
    private RunSession(bool live) { }
    internal static RunSession Live() => new(true);
    static IEnumerable<T> Visible<T>(Node root) where T : Control =>
        root.FindChildren("*", "", true, false).OfType<T>().Where(n => n.IsVisibleInTree());

    internal string WaitReason { get; private set; } = T("waiting");

    internal Decision? Inspect()
    {
        _selecting = null;
        _selected.Clear();
        WaitReason = T("waiting");
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (currentScreen is NMapScreen { IsOpen: true } map)
        {
            if (map.IsTraveling) { WaitReason = T("travel"); return null; }
            if (!map.IsTravelEnabled) {
                WaitReason = T("mapView");
                return null;
            }
            var d = new Decision(DecisionKind.Map);
            foreach (var button in Visible<NMapPoint>(map).Where(x => x.State == MapPointState.Travelable).OrderBy(x => x.Point.coord.col))
            {
                var p = button.Point;
                d.Add(ActionKind.MapMove, T("mapMove", p.coord.row+1, p.coord.col+1, MapLabel(p.PointType)), () => {}, a:p);
            }
            if (d.Choices.Count == 0) WaitReason = "選択できる移動先を待っています";
            return d;
        }
        if (currentScreen is NChooseACardSelectionScreen discovery) return DiscoveryChoice(discovery);
        if (currentScreen is NCardGridSelectionScreen grid) return GridChoice(grid);
        if (currentScreen is NChooseABundleSelectionScreen bundles) return BundleChoice(bundles);
        if (currentScreen is NCardRewardSelectionScreen cards)
        {
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var choices = typeof(NCardRewardSelectionScreen).GetField("_options", fields)?.GetValue(cards) as IReadOnlyList<CardCreationResult>;
            var alternatives = typeof(NCardRewardSelectionScreen).GetField("_extraOptions", fields)?.GetValue(cards) as IReadOnlyList<CardRewardAlternative>;
            if (choices == null) return null;
            var d = new Decision(DecisionKind.CardReward);
            foreach (var item in choices)
                d.Add(ActionKind.PickCardReward, item.Card.Title, () => {}, a:item.Card);
            foreach (var alt in alternatives ?? [])
                d.Add(ActionKind.CardRewardAlt, alt.Title.GetFormattedText(), () => {}, key:Vocab.Alt(alt.OptionId));
            return d;
        }
        if (currentScreen is NRewardsScreen rewards)
            return LiveRewards(Visible<NRewardButton>(rewards).Select(b => (b.Reward, b.IsEnabled)),
                Visible<NProceedButton>(rewards).Any(b => b.IsEnabled));
        if (NOverlayStack.Instance?.Peek() != null) return null;
        if (CombatManager.Instance.IsInProgress)
        {
            var hand = NPlayerHand.Instance;
            if (hand == null || !hand.IsVisibleInTree()) return null;
            if (hand.IsInCardSelection) return currentScreen is NCombatRoom ? HandChoice(hand) : null;
            if (hand.InCardPlay || CombatManager.Instance.PlayerActionsDisabled || RunManagerBusy()) return null;
            return CombatDecision();
        }
        if (NMerchantRoom.Instance?.Inventory is { IsOpen:true } shop && shop.IsVisibleInTree())
        {
            var d = new Decision(DecisionKind.Shop);
            AddShopEntries(d, shop.Inventory);
            d.Add(ActionKind.Leave, T("finishShop"), () => {});
            AddPotionActions(d);
            return d;
        }
        if (NEventRoom.Instance is { } ev && ev.IsVisibleInTree())
        {
            var d = new Decision(DecisionKind.Event);
            foreach (var b in Visible<NEventOptionButton>(ev).Where(b => b.IsEnabled && !b.Option.IsLocked))
                d.Add(ActionKind.EventOption, b.Option.Title.GetFormattedText(), () => {}, a:b.Option,b:HoverObject(b.Option));
            return d;
        }
        if (NRestSiteRoom.Instance is { } rest && rest.IsVisibleInTree())
        {
            var d = new Decision(DecisionKind.RestSite);
            foreach (var b in Visible<NRestSiteButton>(rest).Where(b => b.IsEnabled && b.Option.IsEnabled))
                d.Add(ActionKind.RestOption, b.Option.Title.GetFormattedText(), () => {}, a:b.Option);
            return d;
        }
        return null;
    }
    static Decision LiveRewards(IEnumerable<(Reward Reward, bool Enabled)> buttons, bool canProceed)
    {
        var decision = new Decision(DecisionKind.Rewards);
        bool full = !Me.HasOpenPotionSlots;
        bool needsSlot = false;
        foreach (var (reward, enabled) in buttons) {
            if (reward.SuccessfullySelected) continue;
            if (full && reward is PotionReward potion) {
                // The model can compare the offered bottle with inventory before a slot opens.
                if (potion.Potion != null) {
                    decision.Shown.Add((potion, Zone.Offer, 0));
                    needsSlot = true;
                }
            } else if (enabled) {
                decision.Add(ActionKind.TakeReward, reward.Description.GetFormattedText(), () => {}, a: reward);
            }
        }
        if (canProceed)
            decision.Add(ActionKind.Leave, needsSlot ? T("leaveRewards") : T("proceed"), () => {});
        AddPotionActions(decision);
        if (needsSlot) AddPotionDiscards(decision);
        return decision;
    }

    static string MapLabel(MapPointType type) => type switch {
        MapPointType.Monster => T("monster"), MapPointType.Elite => T("elite"),
        MapPointType.RestSite => T("rest"), MapPointType.Shop => T("shop"),
        MapPointType.Boss => T("boss"), MapPointType.Unknown => T("unknown"),
        _ when type.ToString() == "Treasure" => T("treasure"), _ => T("destination")
    };
    static bool RunManagerBusy() => MegaCrit.Sts2.Core.Runs.RunManager.Instance.ActionExecutor.IsRunning;
    internal static string Describe(Choice c)
    {
        var target = c.B is Creature t ? (t.IsPlayer ? T("targetSelf") : T("targetEnemy", t.Name, t.CurrentHp, t.CombatId)) : "";
        return c.Kind switch {
            ActionKind.EndTurn => T("endTurn"),
            ActionKind.PlayCard when c.A is CardModel card => T("play", card.Title, target),
            ActionKind.UsePotion when c.A is PotionModel potion => T("drink", potion.Title.GetFormattedText(), target),
            ActionKind.DiscardPotion when c.A is PotionModel discarded => T("discard", Me.GetPotionSlotIndex(discarded) + 1, discarded.Title.GetFormattedText()),
            ActionKind.Buy => T("buy", c.A switch {
                CardModel a => a.Title,
                RelicModel a => a.Title.GetFormattedText(),
                PotionModel a => a.Title.GetFormattedText(),
                _ => T("remove")
            }, c.F0),
            _ => c.Label
        };
    }
}
