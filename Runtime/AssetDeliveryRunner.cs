using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;

namespace com.ktgame.assetdelivery
{
	[AddComponentMenu("")]
	internal sealed class AssetDeliveryRunner : MonoBehaviour
	{
		private static AssetDeliveryRunner _instance;
		private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();

		internal static AssetDeliveryRunner Instance
		{
			get
			{
				if (_instance == null)
				{
					var go = new GameObject("[AssetDelivery]") { hideFlags = HideFlags.NotEditable };
					DontDestroyOnLoad(go);
					_instance = go.AddComponent<AssetDeliveryRunner>();
				}

				return _instance;
			}
		}

		private static int _mainThreadId = -1;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void CaptureMainThread()
		{
			_mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
		}

		internal static void Post(Action action)
		{
			if (action == null) return;

			bool onMainThread = _mainThreadId < 0 || System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId;
			if (onMainThread && !Application.isPlaying)
			{
				action();
				return;
			}

			Pending.Enqueue(action);
		}

		internal static Coroutine Run(IEnumerator routine)
		{
			return Instance.StartCoroutine(routine);
		}

		internal static void Stop(Coroutine coroutine)
		{
			if (coroutine != null && _instance != null) _instance.StopCoroutine(coroutine);
		}

		internal static void ResetStatics()
		{
			while (Pending.TryDequeue(out _))
			{
			}

			_instance = null;
		}

		private void Update()
		{
			while (Pending.TryDequeue(out var action))
			{
				try
				{
					action();
				}
				catch (Exception e)
				{
					Debug.LogException(e);
				}
			}
		}

		private void OnDestroy()
		{
			if (_instance == this) _instance = null;
		}
	}
}
