using System;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.CAM;

namespace CAMAutomation
{
    public class CAMFeatureHandler
    {
        public static CAMFeature[] DetectFeatures(Component component, out bool isDetectionComplete)
        {
            // Retrieve the Bodies
            Body[] bodies = Utilities.NXOpenUtils.GetComponentBodies(component).ToArray();

            return DetectFeatures(bodies, out isDetectionComplete);
        }


        public static CAMFeature[] DetectFeatures(Body[] bodies, out bool isDetectionComplete)
        {
            Part workPart = Utilities.RemoteSession.NXSession.Parts.Work;

            FeatureRecognitionBuilder builder = workPart.CAMSetup.CreateFeatureRecognitionBuilder(null);
            try
            {
                // Options
                builder.RecognitionType = FeatureRecognitionBuilder.RecognitionEnum.Parametric;
                builder.GeometrySearchType = FeatureRecognitionBuilder.GeometrySearch.Selected;
                builder.MapFeatures = false;
                builder.AssignColor = false;
                builder.AddCadFeatureAttributes = false;
                builder.IgnoreWarnings = false;

                // Retrieve all possible registred feature Types
                builder.GetRegisteredFeatureTypes(out string[] featureTypes);

                // Set Feature Types
                builder.SetFeatureTypes(featureTypes);

                // Set the bodies
                builder.SetSearchGeometry(bodies);

                // Find the features
                CAMFeature[] features = builder.FindFeatures();

                // Destroy the builder
                builder.Destroy();

                // Get the Features faces
                Face[] featuresFaces = features.SelectMany(p => p.GetFaces()).ToArray();

                // The feature detection will be considered complete if all the faces in the bodies are found in the feature faces
                isDetectionComplete = true;
                for (int i = 0; i < bodies.Length && isDetectionComplete; ++i)
                {
                    Face[] faces = bodies[i].GetFaces();

                    for (int j = 0; j < faces.Length && isDetectionComplete; ++j)
                    {
                        if (!featuresFaces.Contains(faces[j]))
                        {
                            // Color the face in red
                            faces[j].Color = 186;

                            isDetectionComplete = false;
                        }
                    }
                }

                return features;
            }
            catch (Exception)
            {
                // Destroy the builder
                builder.Destroy();

                isDetectionComplete = false;

                return null;
            }
        }


        public static void UpdateFeatures(CAMFeature[] features)
        {
            foreach (CAMFeature feature in features)
            {
                if (feature.Status != CAMFeature.State.Deleted)
                {
                    feature.ApproveChanges();
                }
            }
        }


        public static void ChangeFeaturesCsys(CAMFeature[] features)
        {
            string[] intactFeatures = new string[]
            {
            "STEP1HOLE",
            "STEP2HOLE",
            "STEP3HOLE",
            "SLOT_RECTANGULAR",
            "SLOT_PARTIAL_RECTANGULAR",
            "POCKET_RECTANGULAR_STRAIGHT",
            "SURFACE_PLANAR",
            "SURFACE_PLANAR_RECTANGULAR"
            };

            // Retrieve the cutoff angle for planar features (in degrees)
            double cutoffAngle = 0.0;
            string cutoffAngleStr = ConfigurationManager.AppSettings["MISUMI_PLANAR_FEATURE_ANGLE_CUTOFF"];
            if (cutoffAngleStr == null || !Double.TryParse(cutoffAngleStr, out cutoffAngle) || cutoffAngle < 0.0 || cutoffAngle > 90.0)
            {
                cutoffAngle = 65.0;  // Default value
            }


            foreach (CAMFeature feature in features)
            {
                bool doChange = false;

                // Csys of SLOT_RECTANGULAR features should not be modified
                if (!intactFeatures.Contains(feature.Type))
                {
                    doChange = true;
                }
                else if (feature.Type == "SURFACE_PLANAR" || feature.Type == "SURFACE_PLANAR_RECTANGULAR")
                {
                    Vector3d featureNormal = GetFeatureNormal(feature);
                    Vector3d globalZAxis = new Vector3d(0.0, 0.0, 1.0);

                    // Is the angle between the feature normal and the global z axis less than cutoff angle ?
                    // In other words, is feature normal inside the cone (Center, Angle) = (globalZAxis, 2.0*cutoffAngle)
                    if (Utilities.MathUtils.InSolidAngle(featureNormal, globalZAxis, Utilities.MathUtils.ToRadians(2.0 * cutoffAngle)))
                    {
                        doChange = true;
                    }
                }

                if (doChange)
                {
                    // Set the unit Csys with the same position as the existing one
                    feature.SetCoordinateSystem(feature.CoordinateSystem.Origin, new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 1.0, 0.0));
                }
            }
        }


        public static Vector3d GetFeatureNormal(CAMFeature feature)
        {
            Matrix3x3 orientation = feature.CoordinateSystem.Orientation.Element;
            return new Vector3d(orientation.Zx, orientation.Zy, orientation.Zz);
        }


        public static bool AreFeaturesEquivalent(CAMFeature feature1, CAMFeature feature2)
        {
            string hashKey1 = GetHashKey(feature1);
            string hashKey2 = GetHashKey(feature2);

            return String.Equals(hashKey1, hashKey2);
        }


        private static string GetHashKey(CAMFeature feature)
        {
            // Retrieve the face tags of all the face prototype related to the feature
            List<Tag> faceTags = new List<Tag>();
            foreach (Face face in feature.GetFaces())
            {
                if (face.IsOccurrence)
                {
                    Face pFace = face.Prototype as Face;
                    if (pFace != null)
                    {
                        faceTags.Add(pFace.Tag);
                    }
                }
                else
                {
                    faceTags.Add(face.Tag);
                }
            }

            // Sort the face tags
            faceTags.Sort();

            // Define the hash uisng separator "||"
            // tag_1 || tag_2 || tag_3 ... || tag_n
            string[] faceTagsStr = faceTags.Select(tag => tag.ToString()).ToArray();
            string hash = String.Join("||", faceTagsStr);

            return hash;
        }


        public CAMFeatureHandler(Component component, CAMFeature[] features)
        {
            m_mainComponent = component;
            m_mainFeatures = features;

            m_componentFeatures = new Dictionary<Component, List<CAMFeature>>();
            m_featuresMap = new Dictionary<string, List<CAMFeature>>();

            // Add the couple (m_mainComponent, m_mainFeatures)
            AddFeatures(m_mainComponent, m_mainFeatures);
        }


        ~CAMFeatureHandler()
        {
            // empty
        }


        public void AddFeatures(Component component, CAMFeature[] features)
        {
            foreach (CAMFeature feature in features)
            {
                AddFeature(component, feature);
            }
        }


        public void AddFeature(Component component, CAMFeature feature)
        {
            // Get the HashKey
            string hashKey = GetHashKey(feature);

            // THe feature will be added only if it is a main feature or related to a main feature (i.e., its hashkey already exist)
            if (IsMainFeature(feature) || m_featuresMap.ContainsKey(hashKey))
            {
                // Add the couple (component, feature) to m_componentFeatures
                AddValueToDictionnary<Component, CAMFeature>(m_componentFeatures, component, feature);

                // Add the couple (hashKey, feature) to m_componentFeatures
                AddValueToDictionnary<string, CAMFeature>(m_featuresMap, hashKey, feature);
            }
        }


        public CAMFeature[] GetFeatures(Component component)
        {
            if (m_componentFeatures.ContainsKey(component))
            {
                return m_componentFeatures[component].ToArray();
            }
            else
            {
                return null;
            }
        }


        public CAMFeature[] GetUnusedFeatures(Component component)
        {
            if (m_componentFeatures.ContainsKey(component))
            {
                List<CAMFeature> unusedFeatures = new List<CAMFeature>();
                foreach (CAMFeature feature in m_componentFeatures[component])
                {
                    if (!IsFeatureUsed(feature))
                    {
                        unusedFeatures.Add(feature);
                    }
                }

                return unusedFeatures.ToArray();
            }
            else
            {
                return null;
            }
        }


        public void CleanObsoleteFeatures()
        {
            List<NCGroup> ncGroups = new List<NCGroup>();
            foreach (CAMFeature feature in Utilities.RemoteSession.NXSession.Parts.Work.CAMFeatures.ToArray())
            {
                if (!IsFeatureToolPathComplete(feature))
                {
                    ncGroups.AddRange(feature.GetGroups());
                }
            }

            if (ncGroups.Count != 0)
            {
                Utilities.NXOpenUtils.DeleteNXObjects(ncGroups.ToArray());
            }
        }


        public void CleanObsoleteOperations()
        {
            List<NXOpen.CAM.Operation> operations = new List<NXOpen.CAM.Operation>();
            foreach (NXOpen.CAM.Operation operation in Utilities.RemoteSession.NXSession.Parts.Work.CAMSetup.CAMOperationCollection.ToArray())
            {
                if (operation.GetToolpathTime() == 0.00)
                {
                    operations.Add(operation);
                }
            }

            if (operations.Count != 0)
            {
                Utilities.NXOpenUtils.DeleteNXObjects(operations.ToArray());
            }
        }


        public bool IsMainFeature(CAMFeature feature)
        {
            return m_mainFeatures.Contains(feature) ? true : false;
        }


        public bool AreAllFeaturesUsed()
        {
            foreach (CAMFeature feature in m_mainFeatures)
            {
                if (!IsFeatureUsed(feature))
                {
                    return false;
                }
            }

            return true;
        }


        public bool IsFeatureUsed(CAMFeature feature)
        {
            CAMFeature usedFeature = null;

            return IsFeatureUsed(feature, out usedFeature);
        }


        public bool IsFeatureUsed(CAMFeature feature, out CAMFeature usedFeature)
        {
            usedFeature = null;

            // Get the Hash key of the feature and retrieve all related features
            string hashKey = GetHashKey(feature);
            if (m_featuresMap.ContainsKey(hashKey))
            {
                CAMFeature[] relatedFeatures = m_featuresMap[hashKey].ToArray();

                // If at least one related feature is used, return true
                // If all related features are not used, return false
                foreach (CAMFeature relatedFeature in relatedFeatures)
                {
                    // Check if the related feature toolpath is complete
                    if (IsFeatureToolPathComplete(relatedFeature))
                    {
                        usedFeature = relatedFeature;
                        return true;
                    }
                }

                return false;
            }
            else
            {
                return false;
            }
        }


        public bool IsFeatureToolPathComplete(CAMFeature feature)
        {
            if (feature.GetGroups().Length != 0)
            {
                // Sometime, toolpath is empty but NXOpen API indicates it is not. So we must check the operation time.
                // A toolpath is considered to be complete if the time of at least one operation is greater than 0.0
                NXOpen.CAM.Operation[] operations = feature.GetOperations();
                if (operations.Length != 0 && operations.Any(p => p.GetToolpathTime() > 0.0))
                {
                    return true;
                }
            }

            return false;
        }


        private void AddValueToDictionnary<K, V>(Dictionary<K, List<V>> dict, K key, V value)
        {
            if (!dict.ContainsKey(key))
            {
                dict.Add(key, new List<V>());
            }

            dict[key].Add(value);
        }


        private Component m_mainComponent;
        private CAMFeature[] m_mainFeatures;

        Dictionary<Component, List<CAMFeature>> m_componentFeatures;
        Dictionary<string, List<CAMFeature>> m_featuresMap;
    }
}
