using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;

namespace SLZ.Serialize {
    public partial class ObjectStore {
        public static ObjectStoreBuilder builder() => new ObjectStoreBuilder();
        public ref struct ObjectStoreBuilder {
            private IEnumerable<KeyValuePair<Type, string>> _builtinTypes;
            private IEnumerable<KeyValuePair<Type, string>> _types;
            private IEnumerable<KeyValuePair<string, string>> _typeRenames;
            private IEnumerable<KeyValuePair<string, IPackable>> _objects;
            private ISet<IPackable> _objectSet;
            private JObject _jsonDocument;

            private ObjectStoreBuilder WithBuiltInTypes(IEnumerable<KeyValuePair<Type, string>> builtinTypes) {
                _builtinTypes = builtinTypes;
                return this;
            }
            
            private ObjectStoreBuilder WithTypes(IEnumerable<KeyValuePair<Type, string>> types) {
                _types = types;
                return this;
            }

            private ObjectStoreBuilder WithTypeRenames(IEnumerable<KeyValuePair<string, string>> typeRenames) {
                _typeRenames = typeRenames;
                return this;
            }

            private ObjectStoreBuilder WithObjects(IEnumerable<KeyValuePair<string, IPackable>> objects) {
                _objects = objects;
                return this;
            }

            private ObjectStoreBuilder WithObjectSet(ISet<IPackable> objectSet) {
                _objectSet = objectSet;
                return this;
            }

            private ObjectStoreBuilder WithJsonDocument(JObject jsonDocument) {
                _jsonDocument = jsonDocument;
                return this;
            }

            [PublicAPI]
            public ObjectStore Build() => 
                new ObjectStore(
                    _builtinTypes,
                    _types,
                    _typeRenames,
                    _objects,
                    _objectSet,
                    _jsonDocument);
        }
    }
}