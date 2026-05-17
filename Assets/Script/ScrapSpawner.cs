using UnityEngine;

public class RandomSpawn : MonoBehaviour
{
    private float _spawnDelay = 2f;
    private float _spawnTimer = 0f;
    [SerializeField] private GameObject Scrap;
    void Update()
    {
        if (_spawnTimer <= 0f)
        {
            _spawnTimer = _spawnDelay;
            Spawn();
        }
        else
        {
            _spawnTimer -= Time.deltaTime;
        }
    }
    private void Spawn()
    {
        float randomValue = Random.Range(0, 10f);
        if (randomValue <= 5)
        {
            Instantiate(Scrap, transform.position, Quaternion.identity);
        }
    }
}
