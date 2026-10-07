// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
namespace HsAuto.Core.Models;
public enum ConstructedActionType
{
    NoOp,
    Wait,
    MulliganKeep,
    MulliganReplace,
    ConfirmMulligan,
    PlayCard,
    PlayCardWithTarget,
    Attack,
    UseHeroPower,
    UseHeroPowerWithTarget,
    UseLocation,
    UseLocationWithTarget,
    TradeCard,
    InternalOption,
    ChooseOption,
    EndTurn,
    CancelPendingInput,
    Concede
}