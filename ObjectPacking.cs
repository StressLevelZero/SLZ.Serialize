// Copyright Stress Level Zero, 2018-present.

using Newtonsoft.Json.Linq;
using System;
using System.Text.RegularExpressions;

namespace SLZ.Serialize {
    public interface IPackable {
        void Pack(ObjectStore store, JObject json);
        void Unpack(ObjectStore store, ObjectId objectId);
    }

    public struct ObjectId : IEquatable<ObjectId> {
        public readonly string objectId;

        public ObjectId(string objectId, bool stripPrefix = false) {
            if (stripPrefix) { this.objectId = Regex.Replace(objectId, "^o:", ""); } else { this.objectId = objectId; }
        }

        #region Equality and Hash

        bool IEquatable<ObjectId>.Equals(ObjectId other) {
            if (ReferenceEquals(this, other)) { return true; }

            if (GetType() != other.GetType()) { return false; }

            return objectId == other.objectId;
        }

        public override bool Equals(object other) {
            return other is ObjectId && (this as IEquatable<ObjectId>).Equals((ObjectId) other);
        }

        public static bool operator ==(ObjectId lhs, ObjectId rhs) {
            return ReferenceEquals(lhs, rhs) ||
                   (!ReferenceEquals(lhs, null) && lhs.Equals(rhs));
        }

        public static bool operator !=(ObjectId lhs, ObjectId rhs) { return !(lhs == rhs); }

        public override int GetHashCode() { return objectId.GetHashCode(); }

        #endregion

        #region ToString

        public override string ToString() { return $"o:{objectId}"; }

        #endregion
    }

    public struct TypeId : IEquatable<TypeId> {
        public readonly string typeId;

        public TypeId(string typeId, bool stripPrefix = false) {
            if (stripPrefix) { this.typeId = Regex.Replace(typeId, "^t:", ""); } else { this.typeId = typeId; }
        }

        #region Equality and Hash

        bool IEquatable<TypeId>.Equals(TypeId other) {
            if (ReferenceEquals(this, other)) { return true; }

            if (GetType() != other.GetType()) { return false; }

            return typeId == other.typeId;
        }

        public override bool Equals(object other) {
            return other is TypeId && (this as IEquatable<TypeId>).Equals((TypeId) other);
        }

        public static bool operator ==(TypeId lhs, TypeId rhs) {
            return ReferenceEquals(lhs, rhs) ||
                   (!ReferenceEquals(lhs, null) && lhs.Equals(rhs));
        }

        public static bool operator !=(TypeId lhs, TypeId rhs) { return !(lhs == rhs); }

        public override int GetHashCode() { return typeId.GetHashCode(); }

        #endregion

        #region ToString

        public override string ToString() { return $"t:{typeId}"; }

        #endregion
    }
}