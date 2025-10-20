using UnityEngine;

namespace HutongGames.PlayMaker.Actions
{

	[ActionCategory("Hollow Knight")]
	public class StopCloneWalker : FsmStateAction
	{
		public FsmOwnerDefault target;
		public bool everyFrame;

		private Clone_Walker walker;

		public override void Reset()
		{
			base.Reset();
			target = new FsmOwnerDefault();
			everyFrame = false;
			walker = null;
		}

		private void ApplyStop(Clone_Walker w)
		{
			w.Stop(Clone_Walker.StopReasons.Controlled);
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
					ApplyStop(walker);
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
				ApplyStop(walker);
			}
		}


	}

}
