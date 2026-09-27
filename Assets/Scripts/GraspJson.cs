using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SortQuest
{
    // The installed Newtonsoft package preserves nullable labels. Public fields only, like JsonUtility;
    // computed Unity properties such as LocalGrasp and IsGood must never enter the wire format.
    public static class GraspJson
    {
        private sealed class FieldsOnly : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type type) =>
                new List<MemberInfo>(type.GetFields(BindingFlags.Instance | BindingFlags.Public));
        }
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        { ContractResolver = new FieldsOnly(), TypeNameHandling = TypeNameHandling.None };
        public static string Write(object value) => JsonConvert.SerializeObject(value, Settings);
        public static T Read<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }

    [Serializable]
    public class GraspFeasibilityAnnotation
    {
        public string record_id;
        public string gripper;
        public int checker_version = GripperCollision.Version;
        public bool feasible;
        public string context = "prefab_on_standin_belt";
        public string Key => record_id + ":" + gripper + ":" + checker_version;
    }
}
