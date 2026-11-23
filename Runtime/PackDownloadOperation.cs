using System;
using UnityEngine;

namespace com.ktgame.assetdelivery
{
	public enum PackDownloadResult
	{
		None,
		Succeeded,
		Failed,
		Canceled,

		Declined,

		NotInBuild,
	}

	public sealed class PackDownloadOperation : CustomYieldInstruction
	{
		private Action<PackDownloadOperation> _completed;

		internal PackDownloadOperation(string packName, bool interactive)
		{
			PackName = packName;
			IsInteractive = interactive;
		}

		public string PackName { get; }
		public bool IsInteractive { get; internal set; }
		public bool IsDone { get; private set; }
		public float Progress { get; private set; }
		public AssetPackStatus Status { get; private set; }
		public long BytesDownloaded { get; private set; }
		public long TotalBytes { get; private set; }
		public PackDownloadResult Result { get; private set; }
		public string Error { get; private set; }
		public bool Succeeded => Result == PackDownloadResult.Succeeded;

		internal bool ConfirmationRequested { get; set; }

		public override bool keepWaiting => !IsDone;

		public event Action<PackDownloadOperation> ProgressChanged;

		public event Action<PackDownloadOperation> Completed
		{
			add
			{
				if (IsDone)
				{
					value?.Invoke(this);
					return;
				}

				_completed += value;
			}
			remove => _completed -= value;
		}

		internal static PackDownloadOperation FromCompleted(string packName)
		{
			var op = new PackDownloadOperation(packName, false);
			op.Progress = 1f;
			op.Status = AssetPackStatus.Completed;
			op.Result = PackDownloadResult.Succeeded;
			op.IsDone = true;
			return op;
		}

		internal void Report(AssetPackProgress progress)
		{
			if (IsDone) return;
			Status = progress.Status;
			Progress = Mathf.Max(Progress, progress.Progress);
			BytesDownloaded = progress.BytesDownloaded;
			TotalBytes = progress.TotalBytes;
			ProgressChanged?.Invoke(this);
		}

		internal void Complete(PackDownloadResult result, string error)
		{
			if (IsDone) return;
			Result = result;
			Error = error;
			if (result == PackDownloadResult.Succeeded)
			{
				Progress = 1f;
				Status = AssetPackStatus.Completed;
			}

			IsDone = true;
			var callback = _completed;
			_completed = null;
			try
			{
				callback?.Invoke(this);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}
	}
}
