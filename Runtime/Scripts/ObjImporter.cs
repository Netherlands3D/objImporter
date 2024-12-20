using System.Collections;
using System.Collections.Generic;
using Netherlands3D.ObjImporter.General;
using Netherlands3D.ObjImporter.General.GameObjectDataSet;
using Netherlands3D.ObjImporter.ParseMTL;
using Netherlands3D.ObjImporter.ParseOBJ;
using UnityEngine;
using UnityEngine.Events;

namespace Netherlands3D.ObjImporter
{
    public class ObjImporter : MonoBehaviour
    {
        StreamreadOBJ objreader;
        ReadMTL mtlreader;
        CreateGameObjectDataSetFromOBJ objectDataCreator;
        CreateGameObjects createGameObjects;
        [HideInInspector]
        public bool createdGameobjectIsMoveable = false;
        public GameObjectDataSet gameObjectData;
        [HideInInspector]
        public Material BaseMaterial;
        [HideInInspector]
        public string objFilePath = "";

        [HideInInspector]
        public string mtlFilePath = "";

        [HideInInspector]
        public string imgFilePath = "";

        bool isbusy = false;
        [HideInInspector]
        public bool createSubMeshes = false;

        private bool needToCancel = false;

        public System.Action<string> currentActivity;
        public System.Action<string> currentAction;
        public System.Action<float> progressPercentage;

        public System.Action<string> alertmessage;
        public System.Action<string> errormessage;

        public System.Action<GameObject> returnObjectTo;

        public UnityEvent<bool> ObjImportSucceeded;
        public UnityEvent<bool> MtlImportSucceeded;

        #region send progress and errors
        void BroadcastCurrentActivity(string value)
        {
            if (currentActivity != null) currentActivity(value);
        }
        void BroadcastCurrentAction(string value)
        {
            if (currentAction != null) currentAction(value);
        }

        void BroadcastProgressPercentage(float value)
        {
            if (progressPercentage != null) progressPercentage(value);
        }

        void BroadcastAlertmessage(string value)
        {
            if (alertmessage != null) alertmessage(value);
        }
        void BroadcastErrormessage(string value)
        {
            if (errormessage != null) errormessage(value);
        }


        #endregion
        //reading obj-file
        public void StartImporting(System.Action<GameObject> returnResultTo)
        {
            needToCancel = false;
            returnObjectTo = returnResultTo;
            if (isbusy)
            {
                return;
            }

            objreader = gameObject.AddComponent<StreamreadOBJ>();
            objreader.broadcastProgressPercentage = BroadcastProgressPercentage;
            objreader.BroadcastErrorMessage = BroadcastErrormessage;

            BroadcastCurrentActivity("obj-bestand inlezen");

            objreader.ReadOBJ(objFilePath, OnOBJRead);
        }

        private void OnDestroy()
        {
            if (objreader) Destroy(objreader);
            if (mtlreader) Destroy(mtlreader);
            if (objectDataCreator) Destroy(objectDataCreator);
            if (createGameObjects) Destroy(createGameObjects);
        }

        public void Cancel()
        {
            needToCancel = true;
            Debug.Log("cancelling StreamReadOBJ");
        }

        private void OnOBJRead(bool succes)
        {
            if (!succes) //something went wrong
            {
                isbusy = false;
                objFilePath = string.Empty;
                mtlFilePath = string.Empty;
                imgFilePath = string.Empty;
                returnObjectTo(null);
                return;
            }
            createdGameobjectIsMoveable = !objreader.ObjectUsesRDCoordinates;
            if (objreader.ObjectUsesRDCoordinates)
            {
                transform.position = objreader.RDOrigin.ToUnity();
            }
            
            ObjImportSucceeded?.Invoke(succes);
            
            if (mtlFilePath != "")
            {
                ReadMtlFile();
            }
            else
            {
                BroadcastAlertmessage("geen mtl-file, alle objecten krijgen de default-kleur");
                CreateGameObjectDataSet();
            }
        }

        public void ReadMtlFile()
        {
            if (mtlreader == null)
            {
                mtlreader = gameObject.AddComponent<ReadMTL>();
                mtlreader.broadcastProgressPercentage = BroadcastProgressPercentage;
            }
            BroadcastCurrentActivity("mtl-file lezen");

            // Do we have an image to use?
            if (imgFilePath.Length != 0)
            {
                mtlreader.AddTexture(System.IO.Path.GetFileName(imgFilePath), imgFilePath);
            }

            mtlreader.StartMTLParse(System.IO.File.ReadAllText(mtlFilePath), OnMTLRead, mtlFilePath);
        }

        private void OnMTLRead(bool succes)
        {
            if (!succes)
            {
                mtlreader = null;
            }
            System.IO.File.Delete(mtlFilePath);
            mtlFilePath = "";
            if (needToCancel)
            {
                Debug.Log("cancelled while reading materialFile");
                returnObjectTo(null);

                return;
            }
            CreateGameObjectDataSet();
            MtlImportSucceeded?.Invoke(succes);
        }


        //create the geometry
        void CreateGameObjectDataSet()
        {
            BroadcastCurrentActivity("geometrie samenstellen");

            if (objectDataCreator == null)
            {
                objectDataCreator = gameObject.AddComponent<CreateGameObjectDataSetFromOBJ>();
                objectDataCreator.broadcastProgressPercentage = BroadcastProgressPercentage;
                objectDataCreator.broadcastCurrentAction = BroadcastCurrentAction;
            }
            objectDataCreator.vertices = objreader.vertices;
            objectDataCreator.normals = objreader.normals;
            objectDataCreator.uvs = objreader.uvs;
            objectDataCreator.broadcastProgressPercentage = progressPercentage;
            if(objreader.ObjectUsesRDCoordinates)
                objectDataCreator.origin = objreader.RDOrigin.ToUnity();

            List<Submesh> submeshes = new List<Submesh>();
            foreach (KeyValuePair<string, Submesh> kvp in objreader.submeshes)
            {
                submeshes.Add(kvp.Value);
            }
            objectDataCreator.submeshes = submeshes;
            objectDataCreator.CreateGameObjectDataSet(OnGameObjectDataSetCreated, !createSubMeshes);
        }

        void OnGameObjectDataSetCreated(GameObjectDataSet gods)
        {
            gameObjectData = gods;
            gods.name = System.IO.Path.GetFileName(objFilePath).Replace(".obj", "");
            objFilePath = "";
            if (needToCancel)
            {
                Debug.Log("cancelled while creating GameObjectDataSet");

                returnObjectTo(null);
                return;
            }
            if (gameObjectData is null)
            {
                //is een error
            }
            // add the materials to the dataset
            if (mtlreader != null)
            {
                gameObjectData.materials = mtlreader.GetMaterialData();
            }

            CreateTheGameObject();
        }


        void CreateTheGameObject()
        {
            BroadcastCurrentActivity("objecten creeren");

            createGameObjects = FindObjectOfType<CreateGameObjects>();
            if (createGameObjects == null)
            {
                createGameObjects = gameObject.AddComponent<CreateGameObjects>();
                createGameObjects.BroadcastProgressPercentage = BroadcastProgressPercentage;
            }
            createGameObjects.Create(gameObjectData, BaseMaterial, OnGameObjectCreated);
        }

        void OnGameObjectCreated(GameObject gameObject)
        {
            returnObjectTo(gameObject);
        }
    }
}
