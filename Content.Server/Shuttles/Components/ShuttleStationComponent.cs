using Robust.Shared.GameStates;

namespace Content.Server.Shuttles.Components
{
    [RegisterComponent]
    public sealed partial class ShuttleStationComponent : Component
    {
        /// <summary>
        /// The station this shuttle belongs to.
        /// </summary>
        [DataField("station")]
        public EntityUid? Station;
    }
}
