using System.Collections;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using RosSharp.RosBridgeClient;

public class RGBDRender_v1 : MonoBehaviour
{
    public RGBDMerger_v1 subscriber;

    Mesh mesh;
    MeshRenderer meshRenderer;
    MeshFilter mf;

    [Header("Point Cloud Appearance")]
    [Tooltip("Disc radius in world units")]
    [Range(0.0005f, 0.1f)]
    public float pointSize = 0.003f;

    [Tooltip("Soft edge width (0 = hard circle, 0.5 = very soft)")]
    [Range(0f, 0.5f)]
    public float softEdge = 0.15f;

    [Header("MAKE SURE THESE LISTS ARE MINIMISED OR EDITOR WILL CRASH")]
    private Vector3[] positions = new Vector3[] {new Vector3(0, 0, 0)};
    private Color[] colours = new Color[] {new Color(1f, 1f, 1f)};
    public Transform offset;

    void Start()
    {
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        mesh = new Mesh();
        mf = gameObject.AddComponent<MeshFilter>();
        meshRenderer.material = new Material(Shader.Find("Custom/PointCloudGeometryShader"));
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        transform.position = offset.position;
        transform.rotation = offset.rotation;
    }

    void UpdateMesh()
    {
        mesh.Clear();
        positions = subscriber.GetPCL(3);
        colours = subscriber.GetPCLColor(3);

        if (positions == null || colours == null)
        {
            return;
        }

        mesh.vertices = positions;
        mesh.colors = colours;

        int[] indices = new int[positions.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }
        mesh.SetIndices(indices, MeshTopology.Points, 0);

        mf.mesh = mesh;
    }

    void Update()
    {
        transform.position = offset.position;
        transform.rotation = offset.rotation;

        // Push size parameters to shader
        meshRenderer.material.SetFloat("_PointSize", pointSize);
        meshRenderer.material.SetFloat("_SoftEdge", softEdge);

        UpdateMesh();
    }
}
