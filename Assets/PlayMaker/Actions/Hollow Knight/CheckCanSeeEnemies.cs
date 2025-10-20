using UnityEngine;
using HutongGames.PlayMaker;

namespace HutongGames.PlayMaker.Actions
{

	[ActionCategory("Hollow Knight")]
	public class CheckCanSeeEnemies : FsmStateAction
	{
		[UIHint(UIHint.Variable)]
		public FsmBool storeResult;
		public bool everyFrame;
		private LineOfSightDetector source;

		public override void Reset()
		{
			storeResult = new FsmBool();
		}

		// Code that runs on entering the state.
		public override void OnEnter()
		{
			source = Owner.GetComponent<LineOfSightDetector>();
			if (source != null)
			{
				source.SetDetectEnemies(true);
			}
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
			if (source != null)
			{
				storeResult.Value = source.CanSeeHero;
				return;
			}
			storeResult.Value = false;
		}


	}

}
