using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class ScrapSpawner : MonoBehaviour
{
    [SerializeField] private string scrapLayerName = "Scrap";
    [SerializeField] private bool autoFindScrapObjects = true;
    [FormerlySerializedAs("Scrap")]
    [SerializeField] private GameObject singleScrapPrefab;
    [SerializeField] private GameObject[] scrapPrefabs;
    [SerializeField] private float uniformScale = 1f;
    [SerializeField] private bool useRandomScale = false;
    [SerializeField] private float randomScaleMin = 0.5f;
    [SerializeField] private float randomScaleMax = 0.5f;
    [SerializeField] private int spawnCountPerInterval = 1;
    [SerializeField] private float spawnRadius = 0.5f;
    [SerializeField] private float spawnYOffset = 0f;
    [SerializeField] private bool spawnAsChild = true;
    [SerializeField] private float spawnInterval = 2f;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private float autoDestroyDelay = 10f;

    private readonly List<GameObject> autoFoundScrapSources = new();
    private readonly HashSet<int> spawnedScrapIds = new();
    private float spawnTimer;
    private bool hasLoadedAutoSources;

    private void Awake()
    {
        RefreshAutoFoundScrapSources();
        spawnTimer = Mathf.Max(0f, spawnInterval);
    }

    private void Start()
    {
        if (spawnOnStart)
        {
            SpawnConfiguredCount();
        }
    }

    private void Update()
    {
        if (spawnInterval <= 0f)
        {
            return;
        }

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f)
        {
            return;
        }

        spawnTimer += spawnInterval;
        SpawnConfiguredCount();
    }

    public GameObject SpawnOne()
    {
        var source = PickScrapSource();
        if (source == null)
        {
            Debug.LogWarning("ScrapSpawner could not find a Scrap source to spawn.", this);
            return null;
        }

        var spawnPosition = GetSpawnPosition();
        var spawned = Instantiate(source, spawnPosition, source.transform.rotation);

        ApplyScale(source, spawned);
        CenterVisibleBoundsOn(spawned, spawnPosition);

        if (spawnAsChild)
        {
            spawned.transform.SetParent(transform, true);
        }

        spawnedScrapIds.Add(spawned.GetInstanceID());

        if (autoDestroyDelay > 0f)
        {
            Destroy(spawned, autoDestroyDelay);
        }

        return spawned;
    }

    private void SpawnConfiguredCount()
    {
        var count = Mathf.Max(0, spawnCountPerInterval);
        for (var i = 0; i < count; i++)
        {
            SpawnOne();
        }
    }

    private Vector3 GetSpawnPosition()
    {
        var offset = Vector3.zero;
        if (spawnRadius > 0f)
        {
            var randomPoint = Random.insideUnitCircle * spawnRadius;
            offset = new Vector3(randomPoint.x, 0f, randomPoint.y);
        }

        return transform.position + offset + (Vector3.up * spawnYOffset);
    }

    private GameObject PickScrapSource()
    {
        if (!hasLoadedAutoSources)
        {
            RefreshAutoFoundScrapSources();
        }

        var candidates = new List<GameObject>();
        AddCandidate(candidates, singleScrapPrefab);

        if (scrapPrefabs != null)
        {
            foreach (var scrapPrefab in scrapPrefabs)
            {
                AddCandidate(candidates, scrapPrefab);
            }
        }

        foreach (var source in autoFoundScrapSources)
        {
            AddCandidate(candidates, source);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private void AddCandidate(List<GameObject> candidates, GameObject candidate)
    {
        if (candidate == null || candidate == gameObject)
        {
            return;
        }

        if (spawnedScrapIds.Contains(candidate.GetInstanceID()) || candidate.transform.IsChildOf(transform))
        {
            return;
        }

        if (!candidates.Contains(candidate))
        {
            candidates.Add(candidate);
        }
    }

    private void RefreshAutoFoundScrapSources()
    {
        hasLoadedAutoSources = true;
        autoFoundScrapSources.Clear();

        if (!autoFindScrapObjects)
        {
            return;
        }

        var scrapLayer = LayerMask.NameToLayer(scrapLayerName);
        if (scrapLayer < 0)
        {
            Debug.LogWarning($"ScrapSpawner could not find layer '{scrapLayerName}'.", this);
            return;
        }

#if UNITY_2023_1_OR_NEWER
        var objects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        var objects = FindObjectsOfType<GameObject>();
#endif
        foreach (var foundObject in objects)
        {
            if (foundObject.layer == scrapLayer)
            {
                AddCandidate(autoFoundScrapSources, foundObject);
            }
        }
    }

    private void ApplyScale(GameObject source, GameObject spawned)
    {
        var scaleMultiplier = useRandomScale
            ? Random.Range(Mathf.Min(randomScaleMin, randomScaleMax), Mathf.Max(randomScaleMin, randomScaleMax))
            : uniformScale;

        if (Mathf.Approximately(scaleMultiplier, 1f))
        {
            return;
        }

        spawned.transform.localScale = source.transform.localScale * scaleMultiplier;
    }

    private static void CenterVisibleBoundsOn(GameObject instance, Vector3 targetPosition)
    {
        if (!TryGetVisibleBounds(instance, out var bounds))
        {
            instance.transform.position = targetPosition;
            return;
        }

        var visualOffset = bounds.center - instance.transform.position;
        instance.transform.position = targetPosition - visualOffset;
    }

    private static bool TryGetVisibleBounds(GameObject instance, out Bounds bounds)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (TryCollectBounds(renderers, out bounds))
        {
            return true;
        }

        var colliders = instance.GetComponentsInChildren<Collider>();
        return TryCollectBounds(colliders, out bounds);
    }

    private static bool TryCollectBounds<T>(T[] components, out Bounds bounds) where T : Component
    {
        bounds = default;
        var hasBounds = false;

        foreach (var component in components)
        {
            Bounds componentBounds;
            if (component is Renderer renderer)
            {
                componentBounds = renderer.bounds;
            }
            else if (component is Collider collider)
            {
                componentBounds = collider.bounds;
            }
            else
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = componentBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(componentBounds);
            }
        }

        return hasBounds;
    }
}
