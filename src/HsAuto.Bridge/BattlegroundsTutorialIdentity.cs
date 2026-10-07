using System;

namespace HsAuto.UnityBridge;

internal sealed class BattlegroundsTutorialIdentity
{
	private object? _entity;

	private int _sequence;

	private int _tutorialId;

	public int Observe(object? entity, Func<bool> isNativeTutorial)
	{
		if (entity == _entity)
		{
			return _tutorialId;
		}
		_entity = entity;
		_tutorialId = 0;
		if (entity != null && isNativeTutorial())
		{
			_sequence = ((_sequence == int.MaxValue) ? 1 : (_sequence + 1));
			_tutorialId = _sequence;
		}
		return _tutorialId;
	}

	public bool Matches(int expectedId, object entity)
	{
		if (expectedId > 0 && expectedId == _tutorialId)
		{
			return entity == _entity;
		}
		return false;
	}
}
