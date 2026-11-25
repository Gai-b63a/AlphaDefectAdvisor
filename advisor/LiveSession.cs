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
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Map;
using System.Reflection;

namespace Sts2Headless;

public sealed partial class RunSession
{
    // No selector, bootstrap, Harmony patches or action execution in the retail game.
    private RunSession(bool live) { }
    internal static RunSession Live() => new(true);
    static IEnumerable<T> Visible<T>(Node root) where T : Control =>
        root.FindChildren("*", "", true, false).OfType<T>().Where(n => n.IsVisibleInTree());

    internal Decision? Inspect()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is NCardRewardSelectionScreen cards)
        {
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var choices = typeof(NCardRewardSelectionScreen).GetField("_options", fields)?.GetValue(cards) as IReadOnlyList<CardCreationResult>;
            var alternatives = typeof(NCardRewardSelectionScreen).GetField("_extraOptions", fields)?.GetValue(cards) as IReadOnlyList<CardRewardAlternative>;
            if (choices == null || alternatives == null) return null;
            var d = new Decision(DecisionKind.CardReward);
            foreach (var item in choices)
                d.Add(ActionKind.PickCardReward, item.Card.Title, () => {}, a:item.Card);
            foreach (var alt in alternatives)
                d.Add(ActionKind.CardRewardAlt, alt.Title.GetFormattedText(), () => {}, key:Vocab.Alt(alt.OptionId));
            return d;
        }
        if (overlay is NRewardsScreen rewards)
        {
            var d = new Decision(DecisionKind.Rewards);
            foreach (var b in Visible<NRewardButton>(rewards).Where(b=>b.IsEnabled))
                d.Add(ActionKind.TakeReward,b.Reward.Description.GetFormattedText(),()=>{},a:b.Reward);
            if (Visible<NProceedButton>(rewards).Any(b=>b.IsEnabled))
                d.Add(ActionKind.Leave,"先に進む",()=>{});
            return d;
        }
        if (overlay != null) return null;
        if (NMapScreen.Instance is { IsOpen: true } map)
        {
            if (!map.IsTravelEnabled || map.IsTraveling) return null;
            var d = new Decision(DecisionKind.Map);
            foreach (var button in Visible<NMapPoint>(map).Where(x => x.State == MapPointState.Travelable).OrderBy(x => x.Point.coord.col))
            {
                var p = button.Point;
                d.Add(ActionKind.MapMove, $"{p.coord.row+1}行 {p.coord.col+1}列 · {MapLabel(p.PointType)}", () => {}, a:p);
            }
            return d;
        }
        if (CombatManager.Instance.IsInProgress)
        {
            var hand = NPlayerHand.Instance;
            if (hand == null || !hand.IsVisibleInTree() || hand.IsInCardSelection || hand.InCardPlay || CombatManager.Instance.PlayerActionsDisabled || RunManagerBusy()) return null;
            return CombatDecision();
        }
        if (NMerchantRoom.Instance?.Inventory is { IsOpen:true } shop && shop.IsVisibleInTree())
        {
            var d = new Decision(DecisionKind.Shop);
            AddShopEntries(d, shop.Inventory);
            d.Add(ActionKind.Leave, "買い物を終える", () => {});
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
    static string MapLabel(MapPointType type) => type switch {
        MapPointType.Monster => "通常戦闘", MapPointType.Elite => "エリート",
        MapPointType.RestSite => "休憩所", MapPointType.Shop => "商店",
        MapPointType.Boss => "ボス", MapPointType.Unknown => "未知",
        _ when type.ToString() == "Treasure" => "宝箱", _ => "移動先"
    };
    static bool RunManagerBusy() => MegaCrit.Sts2.Core.Runs.RunManager.Instance.ActionExecutor.IsRunning;
    internal static string Describe(Choice c)
    {
        var target = c.B is Creature t ? (t.IsPlayer ? " → 自分" : $" → {t.Name}（HP {t.CurrentHp}・ID {t.CombatId}）") : "";
        return c.Kind switch {
            ActionKind.EndTurn => "ターン終了",
            ActionKind.PlayCard when c.A is CardModel card => $"{card.Title} を使用{target}",
            ActionKind.UsePotion when c.A is PotionModel potion => $"{potion.Title.GetFormattedText()} を使用{target}",
            ActionKind.DiscardPotion when c.A is PotionModel discarded => $"{discarded.Title.GetFormattedText()} を破棄",
            ActionKind.Buy => (c.A switch {
                CardModel a => a.Title,
                RelicModel a => a.Title.GetFormattedText(),
                PotionModel a => a.Title.GetFormattedText(),
                _ => "カード除去"
            }) + $"（{c.F0} G）",
            _ => c.Label
        };
    }
}
