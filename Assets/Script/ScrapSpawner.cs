using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "스크랩" 레이어를 가진 오브젝트들을 씬에서 자동으로 찾아
/// 랜덤으로 하나를 선택해 주기적으로 스폰하는 스크립트.
/// 이 스크립트는 빈(Empty) 게임오브젝트에 부착합니다.
///
/// [버그 수정 내역 v3]
/// 3. 스크랩이 점점 앞쪽에서 소환되는 버그
///    - 원인A: autoFindScrapObjects 탐색 시 이름에 "(Clone)"이 포함된
///             이미 스폰된 복사본들까지 sourceObjects에 포함됨.
///             Destroy는 예약만 되고 즉시 삭제되지 않으므로,
///             스폰될수록 Clone들이 목록에 쌓이고 그 위치/회전을 기준으로
///             다음 오브젝트가 생성되어 점점 다른 위치로 밀려남.
///    - 원인B: Instantiate 시 source.transform.rotation을 복사해
///             원본/Clone의 회전 방향이 스폰 기준에 영향을 줬음.
///    - 수정: 탐색 시 이름에 "(Clone)"이 포함된 오브젝트 제외.
///            Instantiate 시 회전값을 Quaternion.identity로 고정.
///            sourceObjects는 Awake에서 단 한 번만 확정하고 이후 변경 없음.
/// </summary>
public class ScrapSpawner : MonoBehaviour
{
    // ──────────────────────────────────────────
    // Inspector 노출 필드
    // ──────────────────────────────────────────

    [Header("── 레이어 설정 ──────────────────────")]
    [Tooltip("스크랩 레이어 이름 (Project Settings > Tags and Layers와 일치해야 합니다)")]
    [SerializeField] private string scrapLayerName = "Scrap";

    [Header("── 프리팹 목록 ──────────────────────")]
    [Tooltip("켜두면 씬 내 스크랩 레이어 오브젝트를 자동으로 탐색합니다.\n" +
             "꺼두면 아래 scrapPrefabs 목록에 프리팹을 직접 드래그 앤 드롭하세요.")]
    [SerializeField] private bool autoFindScrapObjects = true;

    [Tooltip("스폰할 프리팹 목록 (autoFindScrapObjects가 꺼진 경우 직접 등록)")]
    [SerializeField] private List<GameObject> scrapPrefabs = new List<GameObject>();

    [Header("── 크기 설정 ──────────────────────")]
    [Tooltip("생성 오브젝트에 적용할 월드 스케일 (1 = 원본 크기)\n" +
             "부모 오브젝트 스케일에 관계없이 항상 이 값이 실제 크기가 됩니다.")]
    [Min(0.01f)]
    [SerializeField] private float uniformScale = 1f;

    [Tooltip("켜두면 Min~Max 범위에서 랜덤 크기를 적용합니다")]
    [SerializeField] private bool useRandomScale = false;

    [Tooltip("랜덤 스케일 최솟값")]
    [Min(0.01f)]
    [SerializeField] private float randomScaleMin = 0.5f;

    [Tooltip("랜덤 스케일 최댓값")]
    [Min(0.01f)]
    [SerializeField] private float randomScaleMax = 1.5f;

    [Header("── 스폰 위치 설정 ──────────────────")]
    [Tooltip("한 번에 생성할 오브젝트 수")]
    [Min(1)]
    [SerializeField] private int spawnCountPerInterval = 1;

    [Tooltip("이 Empty를 중심으로 XZ 평면에서 랜덤 스폰할 반경")]
    [Min(0f)]
    [SerializeField] private float spawnRadius = 5f;

    [Tooltip("스폰 위치의 Y 오프셋 (지면 높이 보정용)")]
    [SerializeField] private float spawnYOffset = 0f;

    [Tooltip("켜두면 생성된 오브젝트를 이 Empty의 자식으로 만듭니다")]
    [SerializeField] private bool spawnAsChild = false;

    [Header("── 반복 생성 설정 ──────────────────")]
    [Tooltip("스폰 주기 (초). 이 간격마다 오브젝트를 생성합니다.")]
    [Min(0.1f)]
    [SerializeField] private float spawnInterval = 2f;

    [Tooltip("켜두면 게임 시작 즉시 첫 스폰 실행 / 꺼두면 첫 주기 대기 후 스폰")]
    [SerializeField] private bool spawnOnStart = true;

    [Header("── 자동 삭제 설정 ──────────────────")]
    [Tooltip("생성된 오브젝트가 이 시간(초) 후 자동 삭제됩니다")]
    [Min(0.1f)]
    [SerializeField] private float autoDestroyDelay = 5f;

    // ──────────────────────────────────────────
    // 내부 상태
    // ──────────────────────────────────────────

    private int              scrapLayerIndex;
    private bool             isSpawning;

    // 원본 오브젝트만 보관 (Clone 절대 포함 안 됨, 게임 중 변경 없음)
    private List<GameObject> sourceObjects = new List<GameObject>();

    // ──────────────────────────────────────────
    // Unity 생명주기
    // ──────────────────────────────────────────

    private void Awake()
    {
        scrapLayerIndex = LayerMask.NameToLayer(scrapLayerName);

        if (scrapLayerIndex == -1)
        {
            Debug.LogError($"[ScrapSpawner] '{scrapLayerName}' 레이어를 찾을 수 없습니다. " +
                           "Edit > Project Settings > Tags and Layers에서 레이어를 추가해 주세요.");
            enabled = false;
            return;
        }

        if (autoFindScrapObjects)
            FindAllScrapObjects();
        else
            sourceObjects = new List<GameObject>(scrapPrefabs);
    }

    private void Start()
    {
        StartSpawning();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        isSpawning = false;
    }

    // ──────────────────────────────────────────
    // 공개 메서드
    // ──────────────────────────────────────────

    /// <summary>씬 내의 스크랩 레이어 원본 오브젝트만 탐색해 목록을 갱신합니다.</summary>
    public void FindAllScrapObjects()
    {
        sourceObjects.Clear();

        GameObject[] all = FindObjectsByType<GameObject>();
        foreach (GameObject obj in all)
        {
            if (obj.layer != scrapLayerIndex) continue;
            if (obj == gameObject)            continue;

            // ── [버그3 핵심 수정A] Clone은 목록에서 완전히 제외 ──
            // Instantiate로 생성된 복사본은 이름에 "(Clone)"이 붙음.
            // Clone이 목록에 들어가면 스폰 기준점이 계속 오염됨.
            if (obj.name.Contains("(Clone)"))  continue;

            sourceObjects.Add(obj);
        }

        Debug.Log($"[ScrapSpawner] 스크랩 원본 {sourceObjects.Count}개 탐색 완료 (Clone 제외).");
    }

    /// <summary>무한 반복 스폰을 시작합니다.</summary>
    public void StartSpawning()
    {
        if (isSpawning) return;
        isSpawning = true;
        StartCoroutine(SpawnLoop());
        Debug.Log("[ScrapSpawner] 스폰 시작.");
    }

    /// <summary>무한 반복 스폰을 중단합니다.</summary>
    public void StopSpawning()
    {
        isSpawning = false;
        StopAllCoroutines();
        Debug.Log("[ScrapSpawner] 스폰 중단.");
    }

    // ──────────────────────────────────────────
    // 핵심 코루틴
    // ──────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        if (!spawnOnStart)
            yield return new WaitForSeconds(spawnInterval);

        while (isSpawning)
        {
            SpawnBatch();
            yield return new WaitForSeconds(spawnInterval);
        }
    }

   private void SpawnBatch()
{
    if (sourceObjects == null || sourceObjects.Count == 0)
    {
        Debug.LogWarning("[ScrapSpawner] 스크랩 원본 목록이 비어 있습니다.");
        return;
    }

    for (int i = 0; i < spawnCountPerInterval; i++)
    {
        GameObject source = GetRandomSource();
        if (source == null) continue;

        Vector3   spawnPos = GetRandomSpawnPosition();
        Transform parent   = spawnAsChild ? transform : null;

        GameObject spawned = Instantiate(source, spawnPos, Quaternion.identity, parent);

        // ── [핵심 수정] Instantiate 직후 위치를 강제로 덮어쓴다 ──────────────
        // Rigidbody / NavMeshAgent / CharacterController 등이
        // Awake·OnEnable에서 위치를 재설정할 수 있으므로
        // 컴포넌트를 일시적으로 제어한 뒤 원하는 위치를 강제 적용한다.
        ForcePosition(spawned, spawnPos);
        // ────────────────────────────────────────────────────────────────────

        SetLayerRecursive(spawned, scrapLayerIndex);

        float targetWorldScale = useRandomScale
            ? Random.Range(randomScaleMin, randomScaleMax)
            : uniformScale;
        ApplyWorldScale(spawned.transform, targetWorldScale);

        Destroy(spawned, autoDestroyDelay);

        Debug.Log($"[ScrapSpawner] '{source.name}' 스폰 " +
                  $"(worldScale={targetWorldScale:F2}, {autoDestroyDelay}초 후 삭제) → {spawnPos}");
    }
}

/// <summary>
/// Rigidbody·NavMeshAgent·CharacterController가 있어도
/// 반드시 spawnPos에 오브젝트를 위치시킵니다.
/// </summary>
private void ForcePosition(GameObject obj, Vector3 worldPos)
{
    // 1) NavMeshAgent — 활성화 상태에서 position 변경 불가
    var agent = obj.GetComponent<UnityEngine.AI.NavMeshAgent>();
    bool agentWasEnabled = false;
    if (agent != null && agent.enabled)
    {
        agent.enabled   = false;
        agentWasEnabled = true;
    }

    // 2) Rigidbody — isKinematic=true로 물리 분리 후 위치 적용
    var rb = obj.GetComponent<Rigidbody>();
    bool wasKinematic = false;
    if (rb != null)
    {
        wasKinematic    = rb.isKinematic;
        rb.isKinematic  = true;
        rb.linearVelocity        = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // 3) 위치 강제 적용 (월드 좌표 기준)
    obj.transform.position = worldPos;

    // 4) Rigidbody 상태 복원 (물리 재활성화)
    if (rb != null)
    {
        rb.isKinematic = wasKinematic;
    }

    // 5) NavMeshAgent 복원 — position 확정 후 다시 켜야 네비메시에 올라탐
    if (agent != null && agentWasEnabled)
    {
        agent.enabled = true;
        // Warp로 네비메시 위에 강제 배치 (NavMesh 범위 밖이면 실패할 수 있음)
        agent.Warp(worldPos);
    }
}
    private void ApplyWorldScale(Transform t, float targetWorldScale)
    {
        t.localScale = Vector3.one; // 원본 스케일 오염 초기화

        if (t.parent != null)
        {
            Vector3 p = t.parent.lossyScale;
            t.localScale = new Vector3(
                targetWorldScale / Mathf.Max(p.x, 0.0001f),
                targetWorldScale / Mathf.Max(p.y, 0.0001f),
                targetWorldScale / Mathf.Max(p.z, 0.0001f)
            );
        }
        else
        {
            t.localScale = Vector3.one * targetWorldScale;
        }
    }

    /// <summary>오브젝트와 모든 자식에 레이어를 재귀 적용합니다.</summary>
    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    // ──────────────────────────────────────────
    // 내부 헬퍼
    // ──────────────────────────────────────────

    private GameObject GetRandomSource()
    {
        // 삭제된 원본이 있으면 제거 (씬에서 직접 지운 경우 대비)
        sourceObjects.RemoveAll(o => o == null);
        if (sourceObjects.Count == 0) return null;
        return sourceObjects[Random.Range(0, sourceObjects.Count)];
    }

    /// <summary>항상 이 Empty의 월드 포지션 기준으로만 랜덤 위치를 계산합니다.</summary>
    private Vector3 GetRandomSpawnPosition()
    {
        Vector2 circle = Random.insideUnitCircle * spawnRadius;
        return transform.position + new Vector3(circle.x, spawnYOffset, circle.y);
    }

    // ──────────────────────────────────────────
    // 에디터 기즈모
    // ──────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * spawnYOffset, spawnRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position + Vector3.up * spawnYOffset, 0.1f);
    }
#endif
}