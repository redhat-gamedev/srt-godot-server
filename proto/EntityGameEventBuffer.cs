// <auto-generated>
//   This file was generated by a tool; you should avoid making direct changes.
//   Consider using 'partial classes' to extend these types
//   Input: EntityGameEventBuffer.proto
// </auto-generated>

#region Designer generated code
#pragma warning disable CS0612, CS0618, CS1591, CS3021, IDE0079, IDE1006, RCS1036, RCS1057, RCS1085, RCS1192
namespace redhatgamedev.srt
{

    [global::ProtoBuf.ProtoContract()]
    public partial class EntityGameEventBuffer : global::ProtoBuf.IExtensible
    {
        private global::ProtoBuf.IExtension __pbn__extensionData;
        global::ProtoBuf.IExtension global::ProtoBuf.IExtensible.GetExtensionObject(bool createIfMissing)
            => global::ProtoBuf.Extensible.GetExtensionObject(ref __pbn__extensionData, createIfMissing);

        [global::ProtoBuf.ProtoMember(1, Name = @"type", IsRequired = true)]
        public EntityGameEventBufferType Type { get; set; } = EntityGameEventBufferType.Unknown;

        [global::ProtoBuf.ProtoMember(2, IsRequired = true)]
        public EntityGameEventBufferObjectType objectType { get; set; } = EntityGameEventBufferObjectType.Unknown;

        [global::ProtoBuf.ProtoMember(3, Name = @"UUID", IsRequired = true)]
        public string Uuid { get; set; }

        [global::ProtoBuf.ProtoMember(4)]
        [global::System.ComponentModel.DefaultValue("")]
        public string ownerUUID
        {
            get => __pbn__ownerUUID ?? "";
            set => __pbn__ownerUUID = value;
        }
        public bool ShouldSerializeownerUUID() => __pbn__ownerUUID != null;
        public void ResetownerUUID() => __pbn__ownerUUID = null;
        private string __pbn__ownerUUID;

        [global::ProtoBuf.ProtoMember(5, Name = @"body")]
        public global::Box2d.PbBody Body { get; set; }

        [global::ProtoBuf.ProtoContract()]
        public enum EntityGameEventBufferType
        {
            [global::ProtoBuf.ProtoEnum(Name = @"UNKNOWN")]
            Unknown = 0,
            [global::ProtoBuf.ProtoEnum(Name = @"CREATE")]
            Create = 1,
            [global::ProtoBuf.ProtoEnum(Name = @"RETRIEVE")]
            Retrieve = 2,
            [global::ProtoBuf.ProtoEnum(Name = @"UPDATE")]
            Update = 3,
            [global::ProtoBuf.ProtoEnum(Name = @"DESTROY")]
            Destroy = 4,
        }

        [global::ProtoBuf.ProtoContract()]
        public enum EntityGameEventBufferObjectType
        {
            [global::ProtoBuf.ProtoEnum(Name = @"UNKNOWN")]
            Unknown = 0,
            [global::ProtoBuf.ProtoEnum(Name = @"PLAYER")]
            Player = 1,
            [global::ProtoBuf.ProtoEnum(Name = @"MISSILE")]
            Missile = 2,
        }

    }

}

#pragma warning restore CS0612, CS0618, CS1591, CS3021, IDE0079, IDE1006, RCS1036, RCS1057, RCS1085, RCS1192
#endregion
