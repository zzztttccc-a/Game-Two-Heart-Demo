using UnityEngine;
using HutongGames.PlayMaker;
using System.Collections.Generic;

namespace HutongGames.PlayMaker.Actions
{

	[ActionCategory("Hollow Knight")]
	public class GetEnemies : FsmStateAction
	{
		[UIHint(UIHint.Variable)]
		[ArrayEditor(VariableType.GameObject)]
		public FsmArray storeResults;

		public bool everyFrame;

		public override void Reset()
		{
			base.Reset();
			storeResults = null;
			everyFrame = false;
		}

		// Code that runs on entering the state.
		public override void OnEnter()
		{
			Apply();
			if (!everyFrame)
			{
				Finish();
			}
		}

		public override void OnUpdate()
		{
			Apply();
		}

		private void Apply()
		{
			var hms = GameObject.FindObjectsOfType<HealthManager>();
			if (hms == null || hms.Length == 0)
			{
				storeResults.Values = new object[0];
				return;
			}

			var list = new List<object>();
			for (int i = 0; i < hms.Length; i++)
			{
				var hm = hms[i];
				if (hm == null) continue;
				if (!hm.gameObject.activeInHierarchy) continue;
				if (hm.isDead) continue;
				list.Add(hm.gameObject);
			}
			storeResults.Values = list.ToArray();
			storeResults.SaveChanges();
		}


	}

}
