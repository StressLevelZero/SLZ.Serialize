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
        private Dictionary<Type, TypeId> types;
        private Dictionary<TypeId, Type> typesReverse;

        private Dictionary<ObjectId, IJSONPackable> objects;
        private HashSet<IJSONPackable> objectSet;

        private JObject jsonDocument;

        private ObjectStore(Dictionary<ObjectId, IJSONPackable> objects, HashSet<IJSONPackable> objectSet,
            JObject jsonDocument) {
            types = new Dictionary<Type, TypeId>();
            typesReverse = new Dictionary<TypeId, Type>();

            this.objects = objects;
            this.objectSet = objectSet;

            this.jsonDocument = jsonDocument;
        }

        public ObjectStore() : this(new Dictionary<ObjectId, IJSONPackable>(), new HashSet<IJSONPackable>(),
            new JObject()) { }

        public ObjectStore(JObject jsonDocument) : this(new Dictionary<ObjectId, IJSONPackable>(),
            new HashSet<IJSONPackable>(), jsonDocument) { }

        public bool TryGetJSON(string key, ObjectId forObject, out JToken result) {
            result = null;
            if (!jsonDocument.TryGetValue("objects", out var objects)) { return false; }

            // Get the object's dictionary
            if (!((JObject) objects).TryGetValue(forObject.ToString(), out var jsonObject)) { return false; }

            // Get the value from the key
            if (!((JObject) jsonObject).TryGetValue(key, out result)) { return false; }

            return true;
        }

        public bool TryUnpackReference<TPackable>(JToken token, ref TPackable packable)
            where TPackable : IJSONPackable {
            // No or invalid JSON to unpack
            var refInfo = token as JObject;
            if (refInfo == null) { return false; }

            // Add the raw packable first.
            // If it needed to be added, unpack. Else it was already there.
            var referencedObjectId = new ObjectId(refInfo["ref"].ToObject<string>(), true);
            if (!objects.ContainsKey(referencedObjectId)) {
                AddOrUpdateObject(referencedObjectId, packable);
                packable.Unpack(this, referencedObjectId);
                return true;
            }

            if (objects[referencedObjectId] is TPackable p) {
                packable = p;
                return true;
            }

            return false;
        }

        public bool TryCreateFromReference<TPackable>(JToken token, out TPackable packable,
            Func<Type, TPackable> factory) where TPackable : IJSONPackable {
            // No or invalid JSON to unpack
            var refInfo = token as JObject;
            if (refInfo == null) {
                packable = default;
                return false;
            }

            // Unknown type id. TODO: relinking
            var referencedTypeId = new TypeId(refInfo["type"].ToObject<string>(), true);
            if (!typesReverse.ContainsKey(referencedTypeId)) {
                packable = default;
                return false;
            }

            // Add the raw packable first.
            // If it needed to be added, create then unpack. Else it was already there.
            var referencedObjectId = new ObjectId(refInfo["ref"].ToObject<string>(), true);
            if (!objects.ContainsKey(referencedObjectId)) {
                packable = factory(typesReverse[referencedTypeId]);
                if (packable == null) { return false; }

                AddOrUpdateObject(referencedObjectId, packable);
                packable.Unpack(this, referencedObjectId);
                return true;
            }

            if (objects[referencedObjectId] is TPackable p) {
                packable = p;
                return true;
            }

            packable = default;
            return false;
        }


        public JObject PackReference<TPackable>(TPackable value) where TPackable : IJSONPackable {
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

        public bool TryPack<TStorable>(TStorable root, out JObject json) where TStorable : IJSONPackable {
            json = new JObject();
            json.Add("version", FORMAT_VERSION);
            json.Add("root", PackReference(root));

            var packedObjects = new HashSet<IJSONPackable>();
            var refsDict = new JObject();
            const int recursionLimit = 8;
            for (var i = 0; i < recursionLimit; i++) {
                var objectsCopy = new Dictionary<ObjectId, IJSONPackable>(objects);
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
            foreach (var entry in types) {
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

        private ObjectId AddObject(IJSONPackable packable) {
            if (objectSet.Contains(packable)) {
                return objects.FirstOrDefault(entry => ReferenceEquals(entry.Value, packable)).Key;
            } else {
                var objectId = NextObjectId();
                objectSet.Add(packable);
                objects[objectId] = packable;
                return objectId;
            }
        }

        private ObjectId AddOrUpdateObject(ObjectId objectId, IJSONPackable packable) {
            if (objects.ContainsKey(objectId)) {
                var previous = objects[objectId];
                objectSet.Remove(previous);
            }

            objectSet.Add(packable);
            objects[objectId] = packable;
            return objectId;
        }

        #endregion

        #region Types

        private int currentTypeId = 0;
        private TypeId NextTypeId() { return new TypeId($"{++currentTypeId}"); }

        private TypeId RegisterTypeId(Type type) {
            if (types.TryGetValue(type, out var typeId)) { return typeId; }

            typeId = NextTypeId();
            types.Add(type, typeId);
            typesReverse.Add(typeId, type);
            return typeId;
        }

        // NOTE: This does not do checking.
        private bool FillTypeId(Type type, TypeId typeId) {
            if (types.ContainsKey(type)) { return types[type] == typeId && typesReverse[typeId] == type; }

            types[type] = typeId;
            typesReverse[typeId] = type;
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