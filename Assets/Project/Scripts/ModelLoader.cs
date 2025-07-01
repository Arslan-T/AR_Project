using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Threading.Tasks;
using GLTFast;
using System;

/// <summary>
/// Handles AR model placement via AR tap or spacebar testing (Editor only),
/// model selection, loading, and manipulation (move, rotate, scale).
/// </summary>
public class ModelLoader : MonoBehaviour
{
    [Header("AR Components")]
    public ARRaycastManager raycastManager;        // Used to raycast AR planes

    [Header("UI Panels")]
    public GameObject modelMenuUI;                 // The model selection menu (circular menu)
    public GameObject controlPanelUI;              // The manipulation controls (move/rotate/scale)

    [Header("Model URLs")]
    public string[] modelUrls = new string[5];     // URLs for loading models dynamically

    private Vector3 placementPosition;             // Where the model should spawn
    private Quaternion placementRotation;          // Orientation for the model
    private GameObject currentModel;               // Reference to the currently loaded model

    private bool waitingForModelSelection = false; // Prevents multiple selections at once

    void Update()
    {
        // Handle AR tap input (for devices)
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            Vector2 touchPosition = Input.GetTouch(0).position;

            // If not already waiting for model selection, try to place model
            if (!waitingForModelSelection)
            {
                TryPlaceOnARPlane(touchPosition);
            }
        }

        // Handle Space key (Editor testing only)
#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (!waitingForModelSelection)
            {
                // Hard-coded placement at origin for testing
                placementPosition = Vector3.zero;
                placementRotation = Quaternion.identity;

                modelMenuUI.SetActive(true);         // Show the circular model selection menu
                waitingForModelSelection = true;     // Now waiting for user to pick a model
            }
        }
#endif
    }

    /// <summary>
    /// Casts a ray against AR planes and shows the model selection menu at valid hit.
    /// </summary>
    /// <param name="touchPosition">Screen position of the touch</param>
    void TryPlaceOnARPlane(Vector2 touchPosition)
    {
        var hits = new System.Collections.Generic.List<ARRaycastHit>();

        // Check if raycast hits an AR plane within polygon bounds
        if (raycastManager.Raycast(touchPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;

            placementPosition = hitPose.position;   // Store where model will be placed
            placementRotation = hitPose.rotation;   // Store orientation

            modelMenuUI.SetActive(true);            // Show model selection menu
            waitingForModelSelection = true;        // Block further placement until user selects
        }
    }

    /// <summary>
    /// Called when user selects a model from the circular UI menu.
    /// </summary>
    /// <param name="index">Index of model URL selected (0-4)</param>
    public void OnModelSelected(int index)
    {
        if (index >= 0 && index < modelUrls.Length)
        {
            modelMenuUI.SetActive(false);          // Hide the menu
            LoadAndPlaceModel(modelUrls[index]);   // Start loading and placing the model
        }
        else
        {
            Debug.LogError("Invalid model index selected");
            waitingForModelSelection = false;      // Allow user to try again
        }
    }

    /// <summary>
    /// Loads the model from a URL and places it at the stored position.
    /// </summary>
    /// <param name="url">The URL to the GLTF/GLB model</param>
    private async void LoadAndPlaceModel(string url)
    {
        // Remove existing model if present
        if (currentModel != null)
        {
            Destroy(currentModel);
        }

        var gltf = new GltfImport();

        // Attempt to load the model
        bool success = await gltf.Load(url);

        if (success)
        {
            // Create container GameObject
            currentModel = new GameObject("ARLoadedModel");
            await gltf.InstantiateMainSceneAsync(currentModel.transform);

            // Set position and rotation
            currentModel.transform.position = placementPosition;
            currentModel.transform.rotation = placementRotation;

            controlPanelUI.SetActive(true);        // Show manipulation controls
        }
        else
        {
            Debug.LogError("Failed to load model from URL: " + url);
        }

        // Done with selection; ready for next placement
        waitingForModelSelection = false;
    }

    // === Manipulation controls ===

    /// <summary>
    /// Moves the current model by delta.
    /// </summary>
    public void Move(Vector3 delta)
    {
        if (currentModel != null)
            currentModel.transform.position += delta;
    }

    /// <summary>
    /// Rotates the current model by euler angles.
    /// </summary>
    public void Rotate(Vector3 euler)
    {
        if (currentModel != null)
            currentModel.transform.Rotate(euler, Space.World);
    }

    /// <summary>
    /// Scales the current model by a factor.
    /// </summary>
    public void Scale(float factor)
    {
        if (currentModel != null)
            currentModel.transform.localScale *= factor;
    }

    // === Move button wrappers ===
    public void MoveUp() { Move(new Vector3(0, 0.05f, 0)); }
    public void MoveDown() { Move(new Vector3(0, -0.05f, 0)); }
    public void MoveLeft() { Move(new Vector3(-0.05f, 0, 0)); }
    public void MoveRight() { Move(new Vector3(0.05f, 0, 0)); }
    public void MoveForward() { Move(new Vector3(0, 0, 0.05f)); }
    public void MoveBack() { Move(new Vector3(0, 0, -0.05f)); }

    // === Rotate button wrappers ===
    public void RotateLeft() { Rotate(new Vector3(0, -15f, 0)); }
    public void RotateRight() { Rotate(new Vector3(0, 15f, 0)); }
}
