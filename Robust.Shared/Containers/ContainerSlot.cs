using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Shared.Containers
{
    [UsedImplicitly]
    [Serializable, NetSerializable]
    public sealed partial class ContainerSlot : BaseContainer
    {
        /// <inheritdoc />
        public override IReadOnlyList<EntityUid> ContainedEntities
        {
            get
            {
                if (_containedEntity == null)
                    return Array.Empty<EntityUid>();

                _containedEntityArray ??= new[] { _containedEntity.Value };
                DebugTools.Assert(_containedEntityArray[0] == _containedEntity);
                return _containedEntityArray;
            }
        }

        [DataField("ent")]
        public EntityUid? ContainedEntity
        {
            get => _containedEntity;
            private set
            {
                _containedEntity = value;
                if (value != null)
                {
                    _containedEntityArray ??= new EntityUid[1];
                    _containedEntityArray[0] = value.Value;
                }
            }
        }

        private EntityUid? _containedEntity;

        // Used by ContainedEntities to avoid allocating.
        [NonSerialized]
        private EntityUid[]? _containedEntityArray;

        /// <inheritdoc />
        public override bool Contains(EntityUid contained)
        {
            if (contained != ContainedEntity)
                return false;

#if DEBUG
            if (IoCManager.Resolve<IGameTiming>().ApplyingState)
                return true;

            var entMan = IoCManager.Resolve<IEntityManager>();
            var flags = entMan.GetComponent<MetaDataComponent>(contained).Flags;
            DebugTools.Assert((flags & MetaDataFlags.InContainer) != 0, $"Entity has bad container flags. Ent: {entMan.ToPrettyString(contained)}. Container: {ID}, Owner: {entMan.ToPrettyString(Owner)}");
#endif
            return true;
        }

        /// <inheritdoc />
        protected override void InternalInsert(EntityUid toInsert, IEntityManager entMan)
        {
            DebugTools.Assert(ContainedEntity == null);

            #if DEBUG
            // TODO make this a proper debug assert when gun code no longer fudges client-side spawn prediction.
            if (toInsert.IsClientSide() && !Owner.IsClientSide() && Manager.NetSyncEnabled)
                Logger.Warning("Inserting a client-side entity into a networked container slot. This will block the container slot and may cause issues.");
            #endif
            ContainedEntity = toInsert;
        }

        /// <inheritdoc />
        protected override void InternalRemove(EntityUid toRemove, IEntityManager entMan)
        {
            DebugTools.Assert(ContainedEntity == toRemove);
            ContainedEntity = null;
        }

        /// <inheritdoc />
        protected override void InternalShutdown(IEntityManager entMan, bool isClient)
        {
            if (ContainedEntity is not { } entity)
                return;

            if (!isClient)
                entMan.DeleteEntity(entity);
            else if (entMan.EntityExists(entity))
                Remove(entity, entMan, reparent: false, force: true);
        }
    }
}
