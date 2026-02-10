using UnityEngine;

namespace Game.Skills
{
    public class SkillEffect : MonoBehaviour
    {
        public float lifetime = 2.0f;
        public float speed = 10f;
        public int damage = 10;
        public Vector2 direction = Vector2.right;

        private void Start()
        {
            Destroy(gameObject, lifetime);
            
            // Adjust direction based on Hero facing?
            // Usually this is handled by the spawner or looking at hero scale
            HeroController hero = FindObjectOfType<HeroController>();
            if (hero != null && !hero.cState.facingRight)
            {
                direction = Vector2.left;
                transform.localScale = new Vector3(-1, 1, 1);
            }
        }

        private void Update()
        {
            transform.Translate(direction * speed * Time.deltaTime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.CompareTag("Enemy")) // Adjust tag as needed
            {
                Debug.Log($"[SkillEffect] Hit {other.name} for {damage} damage!");
                // Apply damage logic here (e.g. HealthManager)
                // other.GetComponent<HealthManager>()?.ApplyDamage(...)
                
                Destroy(gameObject);
            }
        }
    }
}