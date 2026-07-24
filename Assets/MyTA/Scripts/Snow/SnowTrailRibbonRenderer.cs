using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds one continuous strip mesh for the snow trail. Adjacent trail samples
/// share vertices, so the brush RT no longer receives a capsule end for every
/// movement sample.
/// </summary>
[DisallowMultipleComponent]
public sealed class SnowTrailRibbonRenderer : MonoBehaviour
{
    private struct RibbonPoint
    {
        public Vector3 position;
        public Vector3 normal;
        public float distance;
        public bool startsNewStrip;
    }

    private struct RibbonSection
    {
        public Vector3 left;
        public Vector3 right;
        public Vector3 normal;
        public float distance;
        public float depthScale;
        public bool startsNewStrip;
    }

    private readonly List<RibbonPoint> points = new List<RibbonPoint>();
    private readonly List<RibbonSection> sections = new List<RibbonSection>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();

    private Mesh ribbonMesh;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;
    private int maxPoints = 256;
    private float width = 1.2f;
    private float miterLimit = 1.8f;
    private float bevelAngle = 50f;
    private float breakAngle = 135f;
    private float widthVariation = 0.1f;
    private float depthVariation = 0.08f;
    private float variationScale = 1.5f;

    private static readonly int SinkStrengthID = Shader.PropertyToID("_SinkStrength");
    private static readonly int RimStrengthID = Shader.PropertyToID("_RimStrength");
    private static readonly int CenterWidthID = Shader.PropertyToID("_CenterWidth");
    private static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
    private static readonly int OuterSoftnessID = Shader.PropertyToID("_OuterSoftness");
    private static readonly int RibbonModeID = Shader.PropertyToID("_RibbonMode");
    private static readonly int EdgeNoiseStrengthID = Shader.PropertyToID("_EdgeNoiseStrength");
    private static readonly int EdgeNoiseScaleID = Shader.PropertyToID("_EdgeNoiseScale");
    private static readonly int EdgeNoiseDetailID = Shader.PropertyToID("_EdgeNoiseDetail");

    public int PointCount => points.Count;

    public void Initialize(
        Material material,
        string layerName,
        int pointLimit,
        float trailWidth,
        float joinMiterLimit,
        float joinBevelAngle,
        float joinBreakAngle)
    {
        maxPoints = Mathf.Max(4, pointLimit);
        width = Mathf.Max(0.01f, trailWidth);
        SetJoinSettings(pointLimit, joinMiterLimit, joinBevelAngle, joinBreakAngle);

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();

        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = gameObject.AddComponent<MeshRenderer>();

        if (ribbonMesh == null)
        {
            ribbonMesh = new Mesh
            {
                name = "Runtime Snow Trail Ribbon"
            };
            ribbonMesh.MarkDynamic();
        }

        meshFilter.sharedMesh = ribbonMesh;
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0)
            gameObject.layer = layer;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    public void SetJoinSettings(
        int pointLimit,
        float joinMiterLimit,
        float joinBevelAngle,
        float joinBreakAngle)
    {
        maxPoints = Mathf.Max(4, pointLimit);
        miterLimit = Mathf.Max(1f, joinMiterLimit);
        bevelAngle = Mathf.Clamp(joinBevelAngle, 5f, 120f);
        breakAngle = Mathf.Clamp(joinBreakAngle, bevelAngle + 5f, 175f);
    }

    public void SetShape(
        float trailWidth,
        float sinkStrength,
        float rimStrength,
        float centerWidth,
        float edgeWidth,
        float outerSoftness)
    {
        width = Mathf.Max(0.01f, trailWidth);

        if (meshRenderer == null)
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(SinkStrengthID, sinkStrength);
        propertyBlock.SetFloat(RimStrengthID, rimStrength);
        propertyBlock.SetFloat(CenterWidthID, centerWidth);
        propertyBlock.SetFloat(EdgeWidthID, edgeWidth);
        propertyBlock.SetFloat(OuterSoftnessID, outerSoftness);
        propertyBlock.SetFloat(RibbonModeID, 1f);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    public void SetNaturalVariationSettings(
        float pathWidthVariation,
        float pathDepthVariation,
        float pathVariationScale,
        float edgeNoiseStrength,
        float edgeNoiseScale,
        float edgeNoiseDetail)
    {
        widthVariation = Mathf.Clamp(pathWidthVariation, 0f, 0.35f);
        depthVariation = Mathf.Clamp(pathDepthVariation, 0f, 0.35f);
        variationScale = Mathf.Max(0.05f, pathVariationScale);

        if (meshRenderer == null)
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(EdgeNoiseStrengthID, Mathf.Clamp(edgeNoiseStrength, 0f, 0.3f));
        propertyBlock.SetFloat(EdgeNoiseScaleID, Mathf.Max(0.01f, edgeNoiseScale));
        propertyBlock.SetFloat(EdgeNoiseDetailID, Mathf.Clamp01(edgeNoiseDetail));
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    public void AddPoint(Vector3 position, Vector3 surfaceNormal)
    {
        surfaceNormal = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;

        if (points.Count > 0)
        {
            RibbonPoint last = points[points.Count - 1];
            float segmentDistance = Vector3.Distance(last.position, position);

            if (segmentDistance < 0.001f)
                return;

            if (ShouldBreakAtLastPoint(position))
            {
                points.Add(new RibbonPoint
                {
                    position = last.position,
                    normal = last.normal,
                    distance = last.distance,
                    startsNewStrip = true
                });
                last = points[points.Count - 1];
            }

            points.Add(new RibbonPoint
            {
                position = position,
                normal = surfaceNormal,
                distance = last.distance + segmentDistance,
                startsNewStrip = false
            });
        }
        else
        {
            points.Add(new RibbonPoint
            {
                position = position,
                normal = surfaceNormal,
                distance = 0f,
                startsNewStrip = true
            });
        }

        TrimOldPoints();
        RebuildMesh();
    }

    public void BeginNewTrail()
    {
        points.Clear();
        ClearMesh();
    }

    private void TrimOldPoints()
    {
        if (points.Count <= maxPoints)
            return;

        int removeCount = points.Count - maxPoints;
        points.RemoveRange(0, removeCount);

        if (points.Count > 0)
        {
            RibbonPoint first = points[0];
            first.startsNewStrip = true;
            points[0] = first;
        }

        // Keep cumulative distance absolute so variation on surviving points
        // remains fixed when old ribbon points are trimmed.
    }

    private void RebuildMesh()
    {
        if (ribbonMesh == null || points.Count < 2)
        {
            ClearMesh();
            return;
        }

        int pointCount = points.Count;
        sections.Clear();
        vertices.Clear();
        normals.Clear();
        uvs.Clear();
        colors.Clear();
        triangles.Clear();

        for (int i = 0; i < pointCount; i++)
        {
            RibbonPoint point = points[i];
            float halfWidth = width * 0.5f * EvaluateWidthScale(point.distance);
            bool stripStart = point.startsNewStrip || i == 0;
            bool stripEnd = i == pointCount - 1 || points[i + 1].startsNewStrip;

            Vector3 previousDirection = stripStart
                ? GetSegmentDirection(i, Mathf.Min(pointCount - 1, i + 1))
                : GetSegmentDirection(i - 1, i);

            Vector3 nextDirection = stripEnd
                ? previousDirection
                : GetSegmentDirection(i, i + 1);

            if (previousDirection.sqrMagnitude < 0.0001f)
                previousDirection = nextDirection;
            if (nextDirection.sqrMagnitude < 0.0001f)
                nextDirection = previousDirection;
            if (previousDirection.sqrMagnitude < 0.0001f)
                previousDirection = Vector3.forward;
            if (nextDirection.sqrMagnitude < 0.0001f)
                nextDirection = previousDirection;

            Vector3 previousRight = SafeRight(point.normal, previousDirection);
            Vector3 nextRight = SafeRight(point.normal, nextDirection);
            float turnAngle = Vector3.Angle(previousDirection, nextDirection);

            bool useBevel =
                !stripStart &&
                !stripEnd &&
                (turnAngle >= bevelAngle || Vector3.Dot(previousRight, nextRight) <= 0f);

            if (useBevel)
            {
                AddSection(point, previousRight * halfWidth, stripStart);
                AddSection(point, nextRight * halfWidth, false);
                continue;
            }

            Vector3 tangent = (previousDirection + nextDirection).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = nextDirection;

            Vector3 miter = SafeRight(point.normal, tangent);
            float denominator = Mathf.Abs(Vector3.Dot(miter, previousRight));
            float requestedScale = halfWidth / Mathf.Max(denominator, 0.1f);
            float safeScale = Mathf.Min(requestedScale, halfWidth * miterLimit);

            // If the miter had to be heavily clamped, a bevel is safer and avoids
            // long triangular wedges whose interpolated UV can turn into snow spikes.
            if (!stripStart && !stripEnd && requestedScale > halfWidth * miterLimit)
            {
                AddSection(point, previousRight * halfWidth, stripStart);
                AddSection(point, nextRight * halfWidth, false);
            }
            else
            {
                AddSection(point, miter * safeScale, stripStart);
            }
        }

        for (int i = 0; i < sections.Count; i++)
        {
            RibbonSection section = sections[i];
            vertices.Add(section.left);
            vertices.Add(section.right);
            normals.Add(section.normal);
            normals.Add(section.normal);
            uvs.Add(new Vector2(0f, section.distance));
            uvs.Add(new Vector2(1f, section.distance));
            Color vertexData = new Color(section.depthScale, 1f, 1f, 1f);
            colors.Add(vertexData);
            colors.Add(vertexData);
        }

        for (int i = 0; i < sections.Count - 1; i++)
        {
            if (sections[i + 1].startsNewStrip)
                continue;

            int index = i * 2;
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 1);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }

        ribbonMesh.Clear();
        ribbonMesh.SetVertices(vertices);
        ribbonMesh.SetNormals(normals);
        ribbonMesh.SetUVs(0, uvs);
        ribbonMesh.SetColors(colors);
        ribbonMesh.SetTriangles(triangles, 0, true);
        ribbonMesh.RecalculateBounds();
    }

    private bool ShouldBreakAtLastPoint(Vector3 newPosition)
    {
        if (points.Count < 2)
            return false;

        RibbonPoint previous = points[points.Count - 2];
        RibbonPoint last = points[points.Count - 1];

        if (last.startsNewStrip)
            return false;

        Vector3 incoming = Vector3.ProjectOnPlane(last.position - previous.position, last.normal);
        Vector3 outgoing = Vector3.ProjectOnPlane(newPosition - last.position, last.normal);

        if (incoming.sqrMagnitude < 0.0001f || outgoing.sqrMagnitude < 0.0001f)
            return false;

        return Vector3.Angle(incoming, outgoing) >= breakAngle;
    }

    private void AddSection(RibbonPoint point, Vector3 side, bool startsNewStrip)
    {
        sections.Add(new RibbonSection
        {
            left = point.position - side,
            right = point.position + side,
            normal = point.normal,
            distance = point.distance,
            depthScale = EvaluateDepthScale(point.distance),
            startsNewStrip = startsNewStrip
        });
    }

    private float EvaluateWidthScale(float distance)
    {
        float phase = distance / variationScale;
        float broad = Mathf.Sin(phase * 2.17f + 0.73f);
        float detail = Mathf.Sin(phase * 5.03f + 2.11f);
        return Mathf.Max(0.65f, 1f + widthVariation * (broad * 0.72f + detail * 0.28f));
    }

    private float EvaluateDepthScale(float distance)
    {
        float phase = distance / variationScale;
        float broad = Mathf.Sin(phase * 1.61f + 1.37f);
        float detail = Mathf.Sin(phase * 4.37f + 4.19f);
        return Mathf.Max(0.65f, 1f + depthVariation * (broad * 0.75f + detail * 0.25f));
    }

    private static Vector3 SafeRight(Vector3 normal, Vector3 direction)
    {
        Vector3 right = Vector3.Cross(normal, direction);
        return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
    }

    private Vector3 GetSegmentDirection(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex)
            return Vector3.zero;

        Vector3 direction = points[toIndex].position - points[fromIndex].position;
        Vector3 normal = (points[fromIndex].normal + points[toIndex].normal).normalized;
        direction = Vector3.ProjectOnPlane(direction, normal);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private void ClearMesh()
    {
        if (ribbonMesh != null)
            ribbonMesh.Clear();
    }

    private void OnDestroy()
    {
        if (ribbonMesh == null)
            return;

        if (Application.isPlaying)
            Destroy(ribbonMesh);
        else
            DestroyImmediate(ribbonMesh);
    }
}
