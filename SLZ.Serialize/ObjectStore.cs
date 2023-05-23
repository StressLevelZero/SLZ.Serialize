// Copyright Stress Level Zero, 2018-present.

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SLZ.Serialize {
    public class ObjectStore {
        private const bool SLZ_DEBUG_REF_WITH_CLASSNAME = false;
        private const bool SLZ_DEBUG_TYPEINFO = false;

        private const int FORMAT_VERSION = 1;
        private readonly Dictionary<Type, TypeId> _types;
        private readonly Dictionary<TypeId, Type> _typesReverse;

        private readonly Dictionary<ObjectId, IPackable> _objects;
        private readonly HashSet<IPackable> _objectSet;

        private readonly JObject _jsonDocument;

        private ObjectStore(Dictionary<ObjectId, IPackable> objects, HashSet<IPackable> objectSet,
            JObject jsonDocument) {
            _types = new Dictionary<Type, TypeId>();
            _typesReverse = new Dictionary<TypeId, Type>();

            this._objects = objects;
            this._objectSet = objectSet;

            this._jsonDocument = jsonDocument;
        }

        public ObjectStore() : this(new Dictionary<ObjectId, IPackable>(), new HashSet<IPackable>(),
            new JObject()) { }

        public ObjectStore(JObject jsonDocument) : this(new Dictionary<ObjectId, IPackable>(),
            new HashSet<IPackable>(), jsonDocument) { }

        public bool TryGetJSON(string key, ObjectId forObject, out JToken result) {
            result = null;
            if (!_jsonDocument.TryGetValue("objects", out var objects)) { return false; }

            // Get the object's dictionary
            if (!((JObject) objects).TryGetValue(forObject.ToString(), out var jsonObject)) { return false; }

            // Get the value from the key
            if (!((JObject) jsonObject).TryGetValue(key, out result)) { return false; }

            return true;
        }

        public bool TryUnpackReference<TPackable>(JToken token, ref TPackable packable)
            where TPackable : IPackable {
            // No or invalid JSON to unpack
            var refInfo = token as JObject;
            if (refInfo == null) { return false; }

            // Add the raw packable first.
            // If it needed to be added, unpack. Else it was already there.
            var referencedObjectId = new ObjectId(refInfo["ref"].ToObject<string>(), true);
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

        public bool TryCreateFromReference<TPackable>(JToken token, out TPackable packable,
            Func<Type, TPackable> factory) where TPackable : IPackable {
            // No or invalid JSON to unpack
            var refInfo = token as JObject;
            if (refInfo == null) {
                packable = default;
                return false;
            }

            // Unknown type id. TODO: relinking
            var referencedTypeId = new TypeId(refInfo["type"].ToObject<string>(), true);
            if (!_typesReverse.ContainsKey(referencedTypeId)) {
                packable = default;
                return false;
            }

            // Add the raw packable first.
            // If it needed to be added, create then unpack. Else it was already there.
            var referencedObjectId = new ObjectId(refInfo["ref"].ToObject<string>(), true);
            if (!_objects.ContainsKey(referencedObjectId)) {
                packable = factory(_typesReverse[referencedTypeId]);
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


        public JObject PackReference<TPackable>(TPackable value) where TPackable : IPackable {
            var objectId = AddObject(value);

#pragma warning disable CS0162 // Unreachable code detected
            if (!SLZ_DEBUG_REF_WITH_CLASSNAME) {
                return new JObject {
                    ["ref"] = objectId.ToString(),
                    ["type"] = RegisterTypeId(value.GetType()).ToString(),
                };
            } else {
                return new JObject {
                    ["ref"] = objectId.ToString(),
                    ["type"] = RegisterTypeId(value.GetType()).ToString(),
                    ["typeName"] = value.GetType().Name,
                };
            }
#pragma warning restore CS0162 // Unreachable code detected
        }

        public bool TryPack<TStorable>(TStorable root, out JObject json) where TStorable : IPackable {
            json = new JObject();
            json.Add("version", FORMAT_VERSION);
            json.Add("root", PackReference(root));

            var packedObjects = new HashSet<IPackable>();
            var refsDict = new JObject();
            const int recursionLimit = 8;
            for (var i = 0; i < recursionLimit; i++) {
                var objectsCopy = new Dictionary<ObjectId, IPackable>(_objects);
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
                    refsDict[packableId.ToString()] = objDict;
                }
            }

            json.Add("objects", refsDict);

            var typesDict = new JObject();
            foreach (var entry in _types) {
                var type = entry.Key;
                var typeId = entry.Value;
                typesDict[typeId.ToString()] = MakeTypeInfo(type, true);
            }

            json.Add("types", typesDict);

            return true;
        }

        #region Objects

        private int currentObjectId = 0;
        private ObjectId NextObjectId() { return new ObjectId($"{++currentObjectId}"); }

        private ObjectId AddObject(IPackable packable) {
            if (_objectSet.Contains(packable)) {
                return _objects.FirstOrDefault(entry => ReferenceEquals(entry.Value, packable)).Key;
            } else {
                var objectId = NextObjectId();
                _objectSet.Add(packable);
                _objects[objectId] = packable;
                return objectId;
            }
        }

        private ObjectId AddOrUpdateObject(ObjectId objectId, IPackable packable) {
            if (_objects.ContainsKey(objectId)) {
                var previous = _objects[objectId];
                _objectSet.Remove(previous);
            }

            _objectSet.Add(packable);
            _objects[objectId] = packable;
            return objectId;
        }

        #endregion

        #region Types

        private int currentTypeId = 0;
        private TypeId NextTypeId() { return new TypeId($"{++currentTypeId}"); }

        private TypeId RegisterTypeId(Type type) {
            if (_types.TryGetValue(type, out var typeId)) { return typeId; }

            typeId = NextTypeId();
            _types.Add(type, typeId);
            _typesReverse.Add(typeId, type);
            return typeId;
        }

        // NOTE: This does not do checking.
        private bool FillTypeId(Type type, TypeId typeId) {
            if (_types.ContainsKey(type)) { return _types[type] == typeId && _typesReverse[typeId] == type; }

            _types[type] = typeId;
            _typesReverse[typeId] = type;
            return true;
        }

        public void LoadTypes(JObject types) {
            if (types == null) { return; }

            foreach (var typeObj in types) {
                var typeId = new TypeId(typeObj.Key, true);
                var typeName = typeObj.Value["fullname"].ToString();
                var type = Type.GetType(typeName);
                if (type == null) {
                    Console.Error.WriteLine($"Did not find type for type: \"{typeName}\".");
                    continue;
                }

                FillTypeId(type, typeId);
            }
        }

        private JObject MakeTypeInfo(Type type, bool extendedInfo = false) {
            var typeId = RegisterTypeId(type);

            var refInfo = new JObject();
            refInfo["type"] = typeId.ToString();
#pragma warning disable CS0162 // Unreachable code detected
            if (SLZ_DEBUG_REF_WITH_CLASSNAME) { refInfo["typeName"] = type.Name; }
#pragma warning restore CS0162 // Unreachable code detected

            if (extendedInfo) {
                //refInfo["fullname"] = type.FullName;
                refInfo["fullname"] = type.AssemblyQualifiedName;

#pragma warning disable CS0162 // Unreachable code detected
                if (SLZ_DEBUG_TYPEINFO) {
                    refInfo["debug_asqd"] = type.AssemblyQualifiedName;
                    refInfo["debug_is_gtd"] = type.IsGenericTypeDefinition;
                    refInfo["debug_is_gt"] = type.IsGenericType;

                    // NOTE: while this could be done recursively correctly, this is just for debug
                    var shallowTypeArgs = type.GetGenericArguments().Select(arg => arg.FullName).ToList<string>();
                    if (shallowTypeArgs.Count() != 0) {
                        var typeArgsJSON = new JArray(from arg in shallowTypeArgs select new JValue(arg));
                        refInfo["debug_gta"] = typeArgsJSON;
                    }
                }
#pragma warning restore CS0162 // Unreachable code detected
            }

            return refInfo;
        }

        #endregion
    }
}