// Copyright Stress Level Zero, 2018-present.

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace SLZ.Serialize {
    public class ObjectStore {
        private const int FORMAT_VERSION = 1;
        
        private readonly Dictionary<Type, string> _types;
        private readonly Dictionary<string, Type> _typesReverse;

        private readonly Dictionary<string, IPackable> _objects;
        private readonly HashSet<IPackable> _objectSet;

        private readonly JObject _jsonDocument;

        private readonly Dictionary<string, string> _typeRenames;
        
        private readonly Dictionary<Type, string> _builtinTypes;
        private readonly Dictionary<string, Type> _builtinTypesReverse;

        private ObjectStore(Dictionary<string, IPackable> objects, HashSet<IPackable> objectSet,
            JObject jsonDocument) {
            _types = new Dictionary<Type, string>();
            _typesReverse = new Dictionary<string, Type>();

            _typeRenames = new Dictionary<string, string>();
            
            _builtinTypes = new Dictionary<Type, string>();
            _builtinTypesReverse = new Dictionary<string, Type>();
            
            this._objects = objects;
            this._objectSet = objectSet;

            this._jsonDocument = jsonDocument;
        }

        [PublicAPI]
        public ObjectStore() : this(new Dictionary<string, IPackable>(), new HashSet<IPackable>(),
            new JObject()) { }

        [PublicAPI]
        public ObjectStore(JObject jsonDocument) : this(new Dictionary<string, IPackable>(),
            new HashSet<IPackable>(), jsonDocument) { } 

        [Obsolete("TODO: move this to a builder pattern of some sort")]
        [PublicAPI]
        public void AddBuiltins(Dictionary<Type, string> builtins) {
            foreach (var (type, typeId) in builtins) {
                _builtinTypes[type] = typeId;
                _builtinTypesReverse[typeId] = type;
            }
        }
        
        [Obsolete("TODO: move this to a builder pattern of some sort")]
        [PublicAPI]
        public void AddRenames(Dictionary<string, string> renames) {
            foreach (var (from, to) in renames) { _typeRenames[from] = to; }
        }
        
        [PublicAPI]
        public bool TryGetJSON(string key, string objectId, out JToken result) {
            result = null;
            if (!_jsonDocument.TryGetValue("objects", out var objs)) { return false; }

            // Get the object's dictionary
            if (!((JObject) objs).TryGetValue(objectId, out var jsonObject)) { return false; }

            // Get the value from the key
            if (!((JObject) jsonObject).TryGetValue(key, out result)) { return false; }

            return true;
        }

        [PublicAPI]
        public bool TryUnpackReference<TPackable>(JToken token, ref TPackable packable)
            where TPackable : IPackable {
            // No or invalid JSON to unpack
            var refInfo = token as JObject;
            if (refInfo == null) { return false; }

            // Add the raw packable first.
            // If it needed to be added, unpack. Else it was already there.
            var referencedObjectId = refInfo["ref"].ToObject<string>();
            if (!_objects.ContainsKey(referencedObjectId)) {
                AddOrUpdateObject(referencedObjectId, packable);
                packable.Unpack(this, referencedObjectId);
                return true;
            }

            if (_objects[referencedObjectId] is TPackable p) {
                packable = p;
                return true;
            }

            return false;
        }

        [PublicAPI]
        public bool TryCreateFromReference<TPackable>(JToken token, out TPackable packable,
            Func<Type, TPackable> factory) where TPackable : IPackable {
            // No or invalid JSON to unpack
            if (!(token is JObject refInfo)) {
                packable = default;
                return false;
            }

            // Unknown type id. TODO: relinking
            var referencedTypeId = refInfo["type"].ToObject<string>();
            if (!TryResolveTypeId(referencedTypeId, out var type, out _, out _)) {
                packable = default;
                return false;
            }

            // Add the raw packable first.
            // If it needed to be added, create then unpack. Else it was already there.
            var referencedObjectId = refInfo["ref"].ToObject<string>();
            if (!_objects.ContainsKey(referencedObjectId)) {
                packable = factory(type);
                if (packable == null) { return false; }

                AddOrUpdateObject(referencedObjectId, packable);
                packable.Unpack(this, referencedObjectId);
                return true;
            }

            if (_objects[referencedObjectId] is TPackable p) {
                packable = p;
                return true;
            }

            packable = default;
            return false;
        }

        [PublicAPI]
        public JObject PackReference<TPackable>(TPackable value) where TPackable : IPackable {
            return new JObject {
                ["ref"] = AddObject(value),
                ["type"] = RegisterTypeId(value.GetType()),
            };
        }

        [PublicAPI]
        public bool TryPack<TStorable>(TStorable root, out JObject json) where TStorable : IPackable {
            json = new JObject {
                {"version", FORMAT_VERSION},
                {"root", PackReference(root)},
            };

            var packedObjects = new HashSet<IPackable>();
            var refsDict = new JObject();
            const int recursionLimit = 8;
            for (var i = 0; i < recursionLimit; i++) {
                var objectsCopy = new Dictionary<string, IPackable>(_objects);
                foreach (var entry in objectsCopy) {
                    var packable = entry.Value;

                    // Skip already-packed objects
                    if (packedObjects.Contains(packable)) { continue; }

                    packedObjects.Add(packable);

                    // Pack the object
                    var objDict = new JObject();
                    var packableId = AddObject(packable);
                    packable.Pack(this, objDict);

                    // Set the packed type (registering if needed)
                    objDict["isa"] = MakeTypeInfo(packable.GetType());

                    // Attach the packed object to the refs dict
                    refsDict[packableId] = objDict;
                }
            }

            json.Add("objects", refsDict);

            var typesDict = new JObject();
            foreach (var (type, typeId) in _types) {
                if (!_builtinTypes.ContainsKey(type)) {
                    typesDict[typeId] = MakeTypeInfo(type, true);
                }
            }

            if (typesDict.Count > 0) { json.Add("types", typesDict); }
            return true;
        }

        #region Objects

        private int currentObjectId = 0;

        private string AddObject(IPackable packable) {
            if (_objectSet.Contains(packable)) {
                return _objects.FirstOrDefault(entry => ReferenceEquals(entry.Value, packable)).Key;
            }

            currentObjectId++;
            var objectId = currentObjectId.ToString();
            _objectSet.Add(packable);
            _objects[objectId] = packable;
            return objectId;
        }

        private string AddOrUpdateObject(string objectId, IPackable packable) {
            if (_objects.TryGetValue(objectId, out var previous)) {
                _objectSet.Remove(previous);
            }

            _objectSet.Add(packable);
            _objects[objectId] = packable;
            return objectId;
        }

        #endregion

        #region Types

        private int currentTypeId = 0;

        private string RegisterTypeId(Type type) {
            // Check builtins first
            if (_builtinTypes.TryGetValue(type, out var id)) { return id; }
            // Then check type dictionary
            if (_types.TryGetValue(type, out id)) { return id; }

            // If not otherwise known, register it and increment
            currentTypeId++;
            var typeId = currentTypeId.ToString();
            _types.Add(type, typeId);
            _typesReverse.Add(typeId, type);
            return typeId;
        }

        [PublicAPI]
        public void LoadTypes(JObject types) {
            if (types == null || types.Count == 0) { return; }

            // ReSharper disable once UseDeconstruction
            foreach (var typeToken in types) {
                var typeListKey = typeToken.Key;

                // The key is a built-in or already-known typeId, and doesn't need to be loaded
                if (TryResolveTypeId(typeListKey, out _, out _, out _)) { continue; }

                Type type = null;
                // All types need to be registered by their key unless they are fully built-in (& none of them are on v1
                // packed objects)
                if (typeToken.Value is JObject typeObj && typeObj.TryGetValue("fullname", out var typeNameToken)) {
                    type = Type.GetType(typeNameToken.ToString());
                    if (type == null) {
                        Console.Error.WriteLine($"Did not find type for: '{typeObj.ToString(Formatting.None)}'.");
                        continue;
                    }
                }

                if (!_types.ContainsKey(type)) {
                    _types[type] = typeListKey;
                    _typesReverse[typeListKey] = type;
                }
            }
        }

        private JToken MakeTypeInfo(Type type, bool extendedInfo = false) {
            var builtIn = _builtinTypes.TryGetValue(type, out var typeId);
            if (!builtIn) { typeId = RegisterTypeId(type); }

            var refInfo = new JObject();
            refInfo["type"] = typeId;
            if (!builtIn && extendedInfo) {
                //refInfo["fullname"] = type.FullName;
                refInfo["fullname"] = type.AssemblyQualifiedName;
            }
            return refInfo;
        }

        private bool TryResolveTypeId(string typeId, out Type type, out bool isBuiltIn, out bool isRenamed) {
            if (_builtinTypesReverse.TryGetValue(typeId, out type)) {
                isBuiltIn = true;
                isRenamed = false;
                return true;
            }

            if (_typesReverse.TryGetValue(typeId, out type)) {
                isBuiltIn = false;
                isRenamed = false;
                return true;
            }

            if (_typeRenames.TryGetValue(typeId, out var renamedTypeId)) {
                isRenamed = true;
                
                if (_builtinTypesReverse.TryGetValue(renamedTypeId, out type)) {
                    isBuiltIn = true;
                    return true;
                }

                if (_typesReverse.TryGetValue(renamedTypeId, out type)) {
                    isBuiltIn = false;
                    return true; 
                }
            }

            isBuiltIn = false;
            isRenamed = false;
            return false;
        }

        #endregion
    }
}