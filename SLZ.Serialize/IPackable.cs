// Copyright Stress Level Zero, 2018-present.

using Newtonsoft.Json.Linq;

namespace SLZ.Serialize {
    public interface IPackable {
        void Pack(ObjectStore store, JObject json);
        void Unpack(ObjectStore store, ObjectId objectId);
    }
}