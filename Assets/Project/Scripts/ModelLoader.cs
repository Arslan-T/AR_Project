using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Threading.Tasks;
using GLTFast;
using System;

/// <summary>
/// Handles AR model placement, model selection, and model manipulation (move, rotate, scale).
/// </summary>
public class ModelLoader : MonoBehaviour
{
    // Reference to ARRaycastManager for detecting AR planes.
    public ARRaycastManager raycastManager;

    // UI GameObject for the model selection menu (with 5 buttons for different models).
    public GameObject modelMenuUI;

    // UI GameObject for the control panel (move/rotate/scale buttons).
    public GameObject controlPanelUI;

    // URLs of the 3D models to load (GLB/GLTF files).
    public string[] modelUrls = new string[5];

    // The position and rotation where the model will be placed.
    private Vector3 placementPosition;
    private Quaternion placementRotation;

    // The currently loaded model in the AR scene.
    private GameObject currentModel;

    // Indicates if we are waiting for the user to select a model after a plane tap.
    private bool waitingForModelSelection = false;

    void Update()
    {
        // Check for a new touch and if we're not already waiting for model selection.
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began && !waitingForModelSelection)
        {
            Vector2 touchPosition = Input.GetTouch(0).position;
            TryPlaceOnARPlane(touchPosition);
        }
    }

    /// <summary>
    /// Attempts to detect an AR plane at the touch position and sets up placement pose.
    /// </summary>
    /// <param name="touchPosition">The screen position of the touch.</param>
    void TryPlaceOnARPlane(Vector2 touchPosition)
    {
        var hits = new System.Collections.Generic.List<ARRaycastHit>();

        // Raycast against AR planes.
        if (raycastManager.Raycast(touchPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;
            placementPosition = hitPose.position;
            placementRotation = hitPose.rotation;

            // Show the model selection menu.
            modelMenuUI.SetActive(true);
            waitingForModelSelection = true;
        }
    }

    /// <summary>
    /// Called when a model button is clicked to select and load a model.
    /// </summary>
    /// <param name="index">Index of the selected model in the modelUrls array.</param>
    public void OnModelSelected(int index)
    {
        if (index >= 0 && index < modelUrls.Length)
        {
            modelMenuUI.SetActive(false);
            LoadAndPlaceModel(modelUrls[index]);
        }
        else
        {
            Debug.LogError("Invalid model index selected");
        }
    }

    /// <summary>
    /// Loads the selected model from the URL and places it at the detected location.
    /// </summary>
    /// <param name="url">URL of the model to load.</param>
    private async void LoadAndPlaceModel(string url)
    {
        // Destroy any previously loaded model.
        if (currentModel != null)
        {
            Destroy(currentModel);
        }

        var gltf = new GltfImport();

        // Attempt to load the model asynchronously.
        bool success = await gltf.Load(url);

        if (success)
        {
            // Create a container GameObject for the loaded model.
            currentModel = new GameObject("ARLoadedModel");
            await gltf.InstantiateMainSceneAsync(currentModel.transform);

            // Set position and rotation at the detected AR plane.
            currentModel.transform.position = placementPosition;
            currentModel.transform.rotation = placementRotation;

            // Show the control panel for manipulating the model.
            controlPanelUI.SetActive(true);
            waitingForModelSelection = false;
        }
        else
        {
            Debug.LogError("Failed to load model from URL: " + url);
            waitingForModelSelection = false;
        }
    }

    /// <summary>
    /// Moves the model by a delta vector.
    /// </summary>
    /// <param name="delta">The amount to move the model.</param>
    public void Move(Vector3 delta)
    {
        if (currentModel != null)
            currentModel.transform.position += delta;
    }

    /// <summary>
    /// Rotates the model by the given euler angles in world space.
    /// </summary>
    /// <param name="euler">Euler angles to rotate the model by.</param>
    public void Rotate(Vector3 euler)
    {
        if (currentModel != null)
            currentModel.transform.Rotate(euler, Space.World);
    }

    /// <summary>
    /// Scales the model uniformly by the given factor.
    /// </summary>
    /// <param name="factor">Scale multiplier (e.g. 1.1 for 10% increase, 0.9 for 10% decrease).</param>
    public void Scale(float factor)
    {
        if (currentModel != null)
            currentModel.transform.localScale *= factor;
    }

    // ====== Wrapper methods for Move buttons ======

    public void MoveUp() { Move(new Vector3(0, 0.05f, 0)); }          // Move model up by 5 cm
    public void MoveDown() { Move(new Vector3(0, -0.05f, 0)); }      // Move model down by 5 cm
    public void MoveLeft() { Move(new Vector3(-0.05f, 0, 0)); }      // Move model left by 5 cm
    public void MoveRight() { Move(new Vector3(0.05f, 0, 0)); }      // Move model right by 5 cm
    public void MoveForward() { Move(new Vector3(0, 0, 0.05f)); }    // Move model forward by 5 cm
    public void MoveBack() { Move(new Vector3(0, 0, -0.05f)); }      // Move model back by 5 cm

    // ====== Wrapper methods for Rotate buttons ======

    public void RotateLeft() { Rotate(new Vector3(0, -15f, 0)); }    // Rotate model left by 15 degrees
    public void RotateRight() { Rotate(new Vector3(0, 15f, 0)); }    // Rotate model right by 15 degrees
}
