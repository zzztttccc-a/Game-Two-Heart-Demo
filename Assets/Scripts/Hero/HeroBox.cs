using System;
using GlobalEnums;
using UnityEngine;

public class HeroBox : MonoBehaviour
{
    public static bool inactive;
    private HeroController heroCtrl;
    private GameObject damagingObject;
    private bool isHitBuffered;
    private int damageDealt;
    private int hazardType;
    private CollisionSide collisionSide;

    private void Start()
    {
	heroCtrl = HeroController.instance;
    }

    private void LateUpdate()
    {
	if (isHitBuffered)
	{
	    ApplyBufferedHit();
	}
    }

    private void OnTriggerEnter2D(Collider2D otherCollider)
    {
	if (!inactive)
	{
	    CheckForDamage(otherCollider);
	}
    }

    private void OnTriggerStay2D(Collider2D otherCollider)
    {
	if (!inactive)
	{
	    CheckForDamage(otherCollider);
	}
    }

    /// <summary>
    /// ͨ�����ַ�������ܵ��˺��ķ���
    /// һ����ͨ��otherCollider.gameObject���Ƿ���һ�����ֽ�"damages_hero"��playmakerFSM
    /// ��һ����ͨ��otherCollider.gameObject�Ƿ��и���DamageHero�Ľű�
    /// </summary>
    /// <param name="otherCollider"></param>
    private void CheckForDamage(Collider2D otherCollider)
    {
	if (!FSMUtility.ContainsFSM(otherCollider.gameObject, "damages_hero"))
	{
	    DamageHero component = otherCollider.gameObject.GetComponent<DamageHero>();
	    if (component != null)
	    {

		damageDealt = component.damageDealt;
		hazardType = component.hazardType;
		damagingObject = otherCollider.gameObject;
		collisionSide = ((damagingObject.transform.position.x > transform.position.x) ? CollisionSide.right : CollisionSide.left);
		if (!IsHitTypeBuffered(hazardType))
		{
		    ApplyBufferedHit();
		    return;
		}
		isHitBuffered = true;
	    }
	    return;
	}
	PlayMakerFSM fsm = FSMUtility.LocateFSM(otherCollider.gameObject, "damages_hero");
	int dealt = FSMUtility.GetInt(fsm, "damageDealt");
	int type = FSMUtility.GetInt(fsm, "hazardType");
	if (otherCollider.transform.position.x > transform.position.x)
	{
	    heroCtrl.TakeDamage(otherCollider.gameObject, CollisionSide.right, dealt, type);
	    return;
	}
	heroCtrl.TakeDamage(otherCollider.gameObject, CollisionSide.left, dealt, type);
    }

    public static bool IsHitTypeBuffered(int hazardType)
    {
	return hazardType == 0;
    }

    /// <summary>
    /// Ӧ�û�����ܻ�������ִ��HeroController��TakeDamage����
    /// </summary>
    private void ApplyBufferedHit()
    {
	heroCtrl.TakeDamage(damagingObject, collisionSide, damageDealt, hazardType);
	isHitBuffered = false;
    }


}
