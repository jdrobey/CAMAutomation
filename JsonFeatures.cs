using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using static CAMAutomation.JsonDecoder;

namespace CAMAutomation
{
    public class JsonFeatures
    {
        public JsonFeatures()
        {
            m_manager = CAMAutomationManager.GetInstance();
        }
        public List<Feature> GetFeatures(string dir)
        {
            string envs_dir = dir;
            var json_dirInfo = new DirectoryInfo(envs_dir);
            var json_Files = json_dirInfo.GetFiles();
            var features = new List<Feature>();
            foreach (var jsonFile in json_Files)
            {
                string path = jsonFile.FullName;
                string jsonText = File.ReadAllText(path);
                if (jsonFile.Name == "env.json")
                {
                    Feature temp = new Feature();
                    temp.featureType= "ENV";
                    temp.EnvDic = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonText);
                    temp.featureID = "ENV";
                    features.Add(temp);
                }
                else if (jsonFile.Name.Contains("feature_"))
                {
                    Feature temp = new Feature();
                    var objFeature = JsonConvert.DeserializeObject<FeatureFile>(jsonText);
                    temp.featureType = objFeature.FeatureType;
                    temp.EnvDic = objFeature.FeatureEnvDic;
                    try { temp.featureID = objFeature.FeatureEnvDic["@FEATURE.ID"]; }
                    catch { }
                    features.Add(temp);
                }
                else
                {
                    m_manager.LogFile.AddMessage($"The file {jsonFile.Name} is not an acceptable file");
                }

            }
            return features;
        }


        

        private class FeatureFile
        {
            [JsonProperty("featureType")]
            public string FeatureType { get; set; }

            [JsonProperty("env")]
            public Dictionary<string, string> FeatureEnvDic { get; set; }
        }

        
        private CAMAutomationManager m_manager;

    }
}
