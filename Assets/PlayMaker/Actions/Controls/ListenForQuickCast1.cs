using UnityEngine;

namespace HutongGames.PlayMaker.Actions
{

	[ActionCategory("Controls")]
	public class ListenForQuickCast1 : FsmStateAction
	{
		// Add PlayMaker event fields and input references (matching ListenForQuickCast style)
		[Tooltip("Where to send the event.")]
		public FsmEventTarget eventTarget;
		public FsmEvent wasPressed;
		public FsmEvent wasReleased;
		public FsmEvent isPressed;
		public FsmEvent isNotPressed;

		private GameManager gm;
		private InputHandler inputHandler;

		public override void Reset()
		{
			eventTarget = null;
		}

		// Code that runs on entering the state.
		public override void OnEnter()
		{
			gm = GameManager.instance;
			inputHandler = gm.GetComponent<InputHandler>();
		}

		public override void OnUpdate()
		{
			if (!gm.isPaused)
			{
				if (inputHandler != null && inputHandler.inputActions != null)
				{
					if (inputHandler.inputActions.quickCast1.WasPressed)
					{
						Fsm.Event(wasPressed);
					}
					if (inputHandler.inputActions.quickCast1.WasReleased)
					{
						Fsm.Event(wasReleased);
					}
					if (inputHandler.inputActions.quickCast1.IsPressed)
					{
						Fsm.Event(isPressed);
					}
					if (!inputHandler.inputActions.quickCast1.IsPressed)
					{
						Fsm.Event(isNotPressed);
					}
				}
			}
		}

	}

}
