using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CaravanSpeedFix
{
    public class kCaravan_PathFollower
    {
        public static int CostToMoveIntoTile(Caravan caravan, int tile, float yearPercent = -1f)
        {
            int normalTicks = 2500;
            double num = (normalTicks + WorldPathGrid.CalculatedCostAt(tile, false, yearPercent)) * ((double)caravan.TicksPerMove / (double)normalTicks);
            return Mathf.Clamp((int)num, 1, 120000);
        }
    }

    [StaticConstructorOnStartup]
    public class kCaravan : Caravan, IIncidentTarget, ITrader, ILoadReferenceable
    {
        private List<Pawn> pawns = new List<Pawn>();

        private Pawn FirstPawnWithExtremeMentalBreak
        {
            get
            {
                var pawnInfo = typeof(kCaravan).BaseType.GetField("pawns", BindingFlags.Instance | BindingFlags.NonPublic);
                this.pawns = (List<Pawn>)pawnInfo.GetValue(this);

                for (int i = 0; i < this.pawns.Count; i++)
                {
                    if (pawns[i].InMentalState && this.pawns[i].MentalStateDef.IsExtreme)
                    {
                        return this.pawns[i];
                    }
                }
                return null;
            }
        }

        private bool AnyPawnHasExtremeMentalBreak
        {
            get
            {
                return this.FirstPawnWithExtremeMentalBreak != null;
            }
        }

        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            if (this.Resting)
            {
                stringBuilder.Append("CaravanResting".Translate());
            }
            else if (this.AnyPawnHasExtremeMentalBreak)
            {
                stringBuilder.Append("CaravanMemberMentalBreak".Translate(new object[]
                {
                    this.FirstPawnWithExtremeMentalBreak.LabelShort
                }));
            }
            else if (this.AllOwnersDowned)
            {
                stringBuilder.Append("AllCaravanMembersDowned".Translate());
            }
            else if (this.pather.Moving)
            {
                if (this.pather.arrivalAction != null)
                {
                    stringBuilder.Append(this.pather.arrivalAction.ReportString);
                }
                else
                {
                    stringBuilder.Append("CaravanTraveling".Translate());
                }
            }
            else
            {
                FactionBase factionBase = CaravanVisitUtility.FactionBaseVisitedNow(this);
                if (factionBase != null)
                {
                    stringBuilder.Append("CaravanVisiting".Translate(new object[]
                    {
                        factionBase.Label
                    }));
                }
                else
                {
                    stringBuilder.Append("CaravanWaiting".Translate());
                }
            }
            if (this.pather.Moving)
            {
                stringBuilder.AppendLine();
                stringBuilder.Append("CaravanEstimatedTimeToDestination".Translate(new object[]
                {
                    CaravanArrivalTimeEstimator.EstimatedTicksToArrive(this, true).ToStringTicksToPeriod(true)
                }));
            }
            if (this.ImmobilizedByMass)
            {
                stringBuilder.AppendLine();
                stringBuilder.Append("CaravanImmobilizedByMass".Translate());
            }
            string text;
            if (CaravanPawnsNeedsUtility.AnyPawnOutOfFood(this, out text))
            {
                stringBuilder.AppendLine();
                stringBuilder.Append("CaravanOutOfFood".Translate());
                if (!text.NullOrEmpty())
                {
                    stringBuilder.Append(" ");
                    stringBuilder.Append(text);
                    stringBuilder.Append(".");
                }
            }
            else
            {
                stringBuilder.AppendLine();
                stringBuilder.Append("CaravanDaysOfFood".Translate(new object[]
                {
                    this.DaysWorthOfFood.ToString("0.#")
                }));
            }
            stringBuilder.AppendLine();
            stringBuilder.AppendLine(string.Concat(new string[]
            {
                "CaravanBaseMovementTime".Translate(),
                ": ",
                ((float)this.TicksPerMove / 2500f).ToString("0.##"),
                " ",
                "CaravanHoursPerTile".Translate()
            }));

            double actualTicksPerMove = ((double)this.TicksPerMove / 2500f) * (2500f + WorldPathGrid.CalculatedCostAt(base.Tile, false, -1f));
            stringBuilder.Append("CurrentTileMovementTime".Translate() + ": " + ((int)actualTicksPerMove).ToStringTicksToPeriod(true));
            return stringBuilder.ToString();
        }
    }



    [StaticConstructorOnStartup]
    internal static class DetourInjector
    {
        static DetourInjector()
        {
            LongEventHandler.QueueLongEvent(Inject, "LibraryStartup", false, null);
        }

        public static void Inject()
        {

            MethodInfo originalMethod = typeof(Caravan_PathFollower).GetMethod("CostToMoveIntoTile", BindingFlags.Static | BindingFlags.Public);
            MethodInfo modifiedMethod = typeof(kCaravan_PathFollower).GetMethod("CostToMoveIntoTile", BindingFlags.Static | BindingFlags.Public);

            MethodInfo cOrig = typeof(Caravan).GetMethod("GetInspectString", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo cMod = typeof(kCaravan).GetMethod("GetInspectString", BindingFlags.Public | BindingFlags.Instance);

            if(Detours.TryDetourFromTo(cOrig, cMod))
            {
                Log.Message("CaravanSpeedFix: display detour successful");
            }

            if (Detours.TryDetourFromTo(originalMethod, modifiedMethod))
            {
                Log.Message("CaravanSpeedFix: actual detour succesful.");
            }
            
        }
    }

    // copied from mad skills mod - https://ludeon.com/forums/index.php?topic=11148.0
    /// <summary>
    /// As seen in Combat Realism.
    /// </summary>
    public static class Detours
    {

        private static List<string> detoured = new List<string>();
        private static List<string> destinations = new List<string>();

        /**
            This is a basic first implementation of the IL method 'hooks' (detours) made possible by RawCode's work;
            https://ludeon.com/forums/index.php?topic=17143.0
            Performs detours, spits out basic logs and warns if a method is detoured multiple times.
        **/
        public static unsafe bool TryDetourFromTo(MethodInfo source, MethodInfo destination)
        {
            // error out on null arguments
            if (source == null)
            {
                Log.Error("Source MethodInfo is null: Detours");
                return false;
            }

            if (destination == null)
            {
                Log.Error("Destination MethodInfo is null: Detours");
                return false;
            }

            // keep track of detours and spit out some messaging
            string sourceString = source.DeclaringType.FullName + "." + source.Name + " @ 0x" + source.MethodHandle.GetFunctionPointer().ToString("X" + (IntPtr.Size * 2).ToString());
            string destinationString = destination.DeclaringType.FullName + "." + destination.Name + " @ 0x" + destination.MethodHandle.GetFunctionPointer().ToString("X" + (IntPtr.Size * 2).ToString());

            detoured.Add(sourceString);
            destinations.Add(destinationString);

            if (IntPtr.Size == sizeof(Int64))
            {
                // 64-bit systems use 64-bit absolute address and jumps
                // 12 byte destructive

                // Get function pointers
                long Source_Base = source.MethodHandle.GetFunctionPointer().ToInt64();
                long Destination_Base = destination.MethodHandle.GetFunctionPointer().ToInt64();

                // Native source address
                byte* Pointer_Raw_Source = (byte*)Source_Base;

                // Pointer to insert jump address into native code
                long* Pointer_Raw_Address = (long*)(Pointer_Raw_Source + 0x02);

                // Insert 64-bit absolute jump into native code (address in rax)
                // mov rax, immediate64
                // jmp [rax]
                *(Pointer_Raw_Source + 0x00) = 0x48;
                *(Pointer_Raw_Source + 0x01) = 0xB8;
                *Pointer_Raw_Address = Destination_Base; // ( Pointer_Raw_Source + 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 )
                *(Pointer_Raw_Source + 0x0A) = 0xFF;
                *(Pointer_Raw_Source + 0x0B) = 0xE0;

            }
            else
            {
                // 32-bit systems use 32-bit relative offset and jump
                // 5 byte destructive

                // Get function pointers
                int Source_Base = source.MethodHandle.GetFunctionPointer().ToInt32();
                int Destination_Base = destination.MethodHandle.GetFunctionPointer().ToInt32();

                // Native source address
                byte* Pointer_Raw_Source = (byte*)Source_Base;

                // Pointer to insert jump address into native code
                int* Pointer_Raw_Address = (int*)(Pointer_Raw_Source + 1);

                // Jump offset (less instruction size)
                int offset = (Destination_Base - Source_Base) - 5;

                // Insert 32-bit relative jump into native code
                *Pointer_Raw_Source = 0xE9;
                *Pointer_Raw_Address = offset;
            }

            // done!
            return true;
        }

    }
}
