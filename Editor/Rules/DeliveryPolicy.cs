using System;
using System.Collections.Generic;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	[Serializable]
	public abstract class DeliveryPolicy
	{
		public abstract DeliveryMode GetDelivery(int packIndex);
	}

	[Serializable]
	public sealed class FastFollowFirstPolicy : DeliveryPolicy
	{
		[Min(0)] public int fastFollowCount = 1;
		public DeliveryMode rest = DeliveryMode.OnDemand;

		public override DeliveryMode GetDelivery(int packIndex)
		{
			return packIndex <= fastFollowCount ? DeliveryMode.FastFollow : rest;
		}
	}

	[Serializable]
	public sealed class FixedDeliveryPolicy : DeliveryPolicy
	{
		public DeliveryMode mode = DeliveryMode.OnDemand;

		public override DeliveryMode GetDelivery(int packIndex) => mode;
	}

	[Serializable]
	public sealed class PerPackDeliveryPolicy : DeliveryPolicy
	{
		public List<DeliveryMode> modes = new List<DeliveryMode>();
		public DeliveryMode fallback = DeliveryMode.OnDemand;

		public override DeliveryMode GetDelivery(int packIndex)
		{
			int i = packIndex - 1;
			return i >= 0 && i < modes.Count ? modes[i] : fallback;
		}
	}
}
