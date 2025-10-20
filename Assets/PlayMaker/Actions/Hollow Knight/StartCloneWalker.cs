using UnityEngine;

namespace HutongGames.PlayMaker.Actions
{

	[ActionCategory("Hollow Knight")]
	public class StartCloneWalker : FsmStateAction
	{
		public FsmOwnerDefault target;
		public FsmBool walkRight;
		public bool everyFrame;

		private Clone_Walker walker;

		public override void Reset()
		{
			base.Reset();
			target = new FsmOwnerDefault();
			walkRight = new FsmBool { UseVariable = true };
			everyFrame = false;
			walker = null;
		}

		private void Apply(Clone_Walker w)
		{
			if (walkRight == null || walkRight.IsNone)
			{
				w.StartMoving();
			}
			else
			{
				w.Go(walkRight.Value ? 1 : -1);
			}
			w.ClearTurnCoolDown();
		}

		// Code that runs on entering the state.
		public override void OnEnter()
		{
			base.OnEnter();
			GameObject safe = target.GetSafe(this);
			if (safe != null)
			{
				walker = safe.GetComponent<Clone_Walker>();
				if (walker != null)
				{
					Apply(walker);
				}
			}
			else
			{
				walker = null;
			}
			if (!everyFrame)
			{
				Finish();
			}
		}

		public override void OnUpdate()
		{
			base.OnUpdate();
			if (walker != null)
			{
				Apply(walker);
			}
		}


	}

}
