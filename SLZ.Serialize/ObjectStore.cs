﻿// Copyright Stress Level Zero, 2018-present.

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace SLZ.Serialize {
    public partial class ObjectStore {
        [PublicAPI]
        public const int FORMAT_VERSION = 2;

        private readonly Dictionary<string, string> _typeRenames;

        private readonly Dictionary<Type, string> _builtinTypes;
        private readonly Dictionary<Type, string> _types;
        private readonly Dictionary<string, Type> _builtinTypesReverse;
        private readonly Dictionary<string, Type> _typesReverse;

        private readonly Dictionary<string, IPackable> _objects;
        private readonly HashSet<IPackable> _objectSet;
        private readonly JObject _jsonDocument;

        private ObjectStore(
            IEnumerable<KeyValuePair<Type, string>> builtinTypes,
            IEnumerable<KeyValuePair<Type, string>> types,
            IEnumerable<KeyValuePair<string, string>> typeRenames,
            IEnumerable<KeyValuePair<string, IPackable>> objects,
            ISet<IPackable> objectSet,
            JObject jsonDocument) {
            _builtinTypes = new Dictionary<Type, string>(builtinTypes);
            _types = new Dictionary<Type, string>(types);
            _typeRenames = new Dictionary<string, string>(typeRenames);

            _objects = new Dictionary<string, IPackable>(objects);
            _objectSet = new HashSet<IPackable>(objectSet);
            _jsonDocument = jsonDocument;

            _builtinTypesReverse = new Dictionary<string, Type>();
            _typesReverse = new Dictionary<string, Type>();

            foreach (var (type, typeId) in _builtinTypes) { _builtinTypesReverse[typeId] = type; }
            foreach (var (type, typeId) in _types) { _typesReverse[typeId] = type; }
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

        /// <summary>
        /// Loads and registers types from a JObject containing serialized type information.
        /// The types are registered in `_types` and `_typesReverse` for quick lookup during deserialization.
        /// </summary>
        /// <param name="types">A JObject containing type information in serialized JSON form.</param>
        [PublicAPI]
        public void LoadTypes(JObject types) {
            // 1. Check if the 'types' JObject is null or empty. If yes, return immediately.
            if (types == null || types.Count == 0) { return; }

            // 2. Iterate over each property in the 'types' JObject. (NOTE: direct iteration seems to skip all entries)
            foreach (var typeProperty in types.Properties()) {
                var typeListKey = typeProperty.Name;

                // 3. If the type ID is built-in or already known, skip the current iteration.
                if (TryResolveTypeId(typeListKey, out _, out _, out _)) { continue; }

                Type type = null;
                
                // 4. If the property value contains the 'fullname' key, attempt to resolve the full name to a Type instance.
                if (typeProperty.Value is JObject typeObj && typeObj.TryGetValue("fullname", out var typeNameToken)) {
                    // 5. If the type resolution is successful and the type wasn't renamed, register the type mapping in
                    // `_types`. Register the reverse mapping in '_typesReverse' regardless.
                    if (TryResolveTypeId(typeNameToken.ToString(), out type, out _, out var renamed)) {
                        if (!renamed) {
                            if (!_types.TryAdd(type, typeListKey)) {
                                Console.Error.WriteLine($"Could not register type '{typeListKey}' as '{type.FullName}'.");
                            }
                        }
                        _typesReverse[typeListKey] = type;
                        continue;
                    }
                    
                    // 6. If the type resolution failed, attempt a final resolution using Type.GetType.
                    type = Type.GetType(typeNameToken.ToString());
                    if (type == null) {
                        Console.Error.WriteLine($"Did not find type for: '{typeObj.ToString(Formatting.None)}'.");
                        continue;
                    }
                }

                // 7. If the resolved type is not present in `_types`, register the type mapping in both `_types` and
                // `_typesReverse`.
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