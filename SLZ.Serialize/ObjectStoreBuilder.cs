using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;

namespace SLZ.Serialize {
    public partial class ObjectStore {
        public static ObjectStoreBuilder Builder => new ObjectStoreBuilder();
        public ref struct ObjectStoreBuilder {
            private IEnumerable<KeyValuePair<Type, string>> _builtinTypes;
            private IEnumerable<KeyValuePair<Type, string>> _types;
            private IEnumerable<KeyValuePair<string, string>> _typeRenames;
            private IEnumerable<KeyValuePair<string, IPackable>> _objects;
            private ISet<IPackable> _objectSet;
            private JObject _jsonDocument;

            [PublicAPI]
            public ObjectStoreBuilder WithBuiltInTypes(IEnumerable<KeyValuePair<Type, string>> builtinTypes) {
                _builtinTypes = builtinTypes;
                return this;
            }

            [PublicAPI]
            public ObjectStoreBuilder WithTypes(IEnumerable<KeyValuePair<Type, string>> types) {
                _types = types;
                return this;
            }

            [PublicAPI]
            public ObjectStoreBuilder WithTypeRenames(IEnumerable<KeyValuePair<string, string>> typeRenames) {
                _typeRenames = typeRenames;
                return this;
            }

            [PublicAPI]
            public ObjectStoreBuilder WithObjects(IEnumerable<KeyValuePair<string, IPackable>> objects) {
                _objects = objects;
                return this;
            }

            [PublicAPI]
            public ObjectStoreBuilder WithObjectSet(ISet<IPackable> objectSet) {
                _objectSet = objectSet;
                return this;
            }

            [PublicAPI]
            public ObjectStoreBuilder WithJsonDocument(JObject jsonDocument) {
                _jsonDocument = jsonDocument;
                return this;
            }

            [PublicAPI]
            public ObjectStore Build() =>
                new ObjectStore(
                    _builtinTypes ?? new Dictionary<Type, string>(),
                    _types ?? new Dictionary<Type, string>(),
                    _typeRenames ?? new Dictionary<string, string>(),
                    _objects ?? new Dictionary<string, IPackable>(),
                    _objectSet ?? new HashSet<IPackable>(),
                    _jsonDocument ?? new JObject());
        }
    }
}