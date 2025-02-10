using Domain;
using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator.Interfaces;

public interface IBatteryEMS : IObservable<BatteryState>
{
    void SetChargeLevel(Power setpoint);
    Power GetChargeLevel();
    Percentage GetSoC();
    Task UpdateState(DateTime time);
    IDisposable Subscribe(IObserver<BatteryState> observer);
    Task PushBatteryState(BatteryState state);
    void EndTransmission();
}