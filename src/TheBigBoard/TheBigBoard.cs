using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(TheBigBoard.TheBigBoard))]

namespace TheBigBoard;

/// <summary>
/// The Big Board: a business command panel for Big Ambitions, using the
/// OFFICIAL API (no MelonLoader/Harmony).
/// Copy the built DLL into ModsLocal and enable it from the in-game mod menu.
/// </summary>
[ModEntryOnInitializationLoad]
public sealed class TheBigBoard : ModBigAmbitionsBase
{
    public override Task OnLoadAsync(ModContext context)
    {
        context.Logger.Info("The Big Board 0.11.0 (beta) loaded.");
        PhoneButtonInstaller.Ensure(context);
        return Task.CompletedTask;
    }

    public override Task OnUnloadAsync()
    {
        PhoneButtonInstaller.Teardown();
        return Task.CompletedTask;
    }
}
