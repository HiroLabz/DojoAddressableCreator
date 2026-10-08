using System.Collections.Generic;

namespace Dojo.Game.UI
{
    /// <summary>What a tour step points at.</summary>
    public enum FtueTarget
    {
        /// <summary>Nothing: the screen stays bright and clickable.</summary>
        None,

        /// <summary>The TAB button, bottom-left.</summary>
        MenuButton,

        /// <summary>The INVENTORY entry on the raised menu.</summary>
        Inventory,

        /// <summary>The SAVE WORLD entry on the raised menu.</summary>
        SaveWorld,

        /// <summary>The SHOP entry on the raised menu.</summary>
        Shop,

        /// <summary>One of the player's agents, standing in the world. The spotlight follows them as they walk.</summary>
        Agent,
    }

    /// <summary>What finishes a step. NEXT always works as well, so nobody is stuck on one.</summary>
    public enum FtueGoal
    {
        /// <summary>Only NEXT.</summary>
        None,

        /// <summary>The camera has panned, rotated and zoomed.</summary>
        LookAround,

        /// <summary>An agent's chat has opened.</summary>
        MeetAgent,

        /// <summary>The menu's entries are up.</summary>
        OpenMenu,

        /// <summary>The inventory is open, which is Edit phase.</summary>
        OpenInventory,

        /// <summary>A piece has been put down.</summary>
        PlacePiece,

        /// <summary>The world has been saved.</summary>
        SaveWorld,

        /// <summary>The last card: its own buttons end the tour.</summary>
        Finish,
    }

    /// <summary>The colour a step is drawn in.</summary>
    public enum FtueAccent
    {
        Signal,
        Shop,
    }

    /// <summary>The icon on a step's card.</summary>
    public enum FtueIcon
    {
        Inventory,
        Camera,
        Agents,
        Menu,
        Place,
        Save,
        Shop,
    }

    /// <summary>One card of the tour.</summary>
    public sealed class FtueStep
    {
        public FtueStep(FtueGoal goal, string title, string body, FtueIcon icon, FtueTarget target,
            FtueAccent accent = FtueAccent.Signal, string secondary = "SKIP TOUR", string primary = "NEXT",
            bool secondaryOpensShop = false, bool needsPointer = false, bool needsAgents = false,
            bool required = false, bool needsInventoryClosed = false, bool needsShop = false)
        {
            Required = required;
            NeedsInventoryClosed = needsInventoryClosed;
            NeedsShop = needsShop;
            Goal = goal;
            Title = title;
            Body = body;
            Icon = icon;
            Target = target;
            Accent = accent;
            Secondary = secondary;
            Primary = primary;
            SecondaryOpensShop = secondaryOpensShop;
            NeedsPointer = needsPointer;
            NeedsAgents = needsAgents;
        }

        /// <summary>What finishes this step, besides NEXT.</summary>
        public FtueGoal Goal { get; }

        public string Title { get; }

        /// <summary>The line under the title. Words in &lt;b&gt; are drawn in the step's highlight.</summary>
        public string Body { get; }

        public FtueIcon Icon { get; }

        public FtueTarget Target { get; }

        public FtueAccent Accent { get; }

        /// <summary>The ghost button: SKIP TOUR, OPEN SHOP on the shop card, or null for none at all.</summary>
        public string Secondary { get; }

        /// <summary>The primary button: NEXT, or START BUILDING on the last card.</summary>
        public string Primary { get; }

        /// <summary>True when the ghost button opens the shop rather than skipping the tour.</summary>
        public bool SecondaryOpensShop { get; }

        /// <summary>
        /// Needs a one-finger tap or drag in the world, which a touchscreen cannot do yet - those
        /// still read the mouse. Left out on a phone rather than asking for the impossible.
        /// </summary>
        public bool NeedsPointer { get; }

        /// <summary>Needs a manager and an agent standing in the world. Left out without them.</summary>
        public bool NeedsAgents { get; }

        /// <summary>
        /// NEXT stays off until the goal is met: the tour does not go on until the player has done
        /// it. SKIP TOUR still works, so nobody is trapped.
        /// </summary>
        public bool Required { get; }

        /// <summary>
        /// The card waits for the inventory to be shut before it comes up, and steps aside if it is
        /// opened again.
        /// </summary>
        public bool NeedsInventoryClosed { get; }

        /// <summary>About the shop, which may not happen. Left out unless the tour is told there is one.</summary>
        public bool NeedsShop { get; }
    }

    /// <summary>
    /// The first-time tour: the three cards from the FTUE mock-ups - the studio floor, the TAB
    /// button, the packs - with the steps between them that teach what they describe, and a last
    /// card to say well done. The packs card is skipped while the shop is not settled.
    /// </summary>
    /// <remarks>
    /// Every step but the first and last finishes by itself when the player does the thing it asks
    /// for, and NEXT is there throughout for anyone who would rather read on.
    /// <para>
    /// Every line is true of the game as it is. The first card's mock promised that the world
    /// "saves itself as you go" with "the last autosave in the top right". It does save itself on
    /// leaving Edit, but only a world that already has a name, and nothing on screen says so - so
    /// the tour has a step for saving on purpose instead.
    /// </para>
    /// </remarks>
    public static class FtueSteps
    {
        /// <summary>Every step, before any is left out for this player.</summary>
        public static IReadOnlyList<FtueStep> All(bool touch)
        {
            return new List<FtueStep>
            {
                new FtueStep(FtueGoal.None,
                    "Welcome to the studio floor",
                    "This is your world. Drop pieces, place agents, and save your floor whenever it "
                        + "looks right. A few steps will show you how.",
                    FtueIcon.Inventory, FtueTarget.None),

                new FtueStep(FtueGoal.LookAround,
                    "Look around",
                    touch
                        ? "Drag with <b>two fingers</b> to pan, <b>twist</b> to rotate and <b>pinch</b> "
                            + "to zoom. Try all three."
                        : "<b>Middle-drag</b> to pan, <b>right-drag</b> to rotate and use the "
                            + "<b>wheel</b> to zoom. Try all three.",
                    FtueIcon.Camera, FtueTarget.None),

                // Required: the tour waits for the agent to be clicked rather than letting NEXT walk
                // past. The spotlight is on one agent; clicking any of them does it.
                new FtueStep(FtueGoal.MeetAgent,
                    "Meet your agent",
                    "<b>Click</b> one of your agents. Your manager walks over and a chat opens, so "
                        + "say hello.",
                    FtueIcon.Agents, FtueTarget.Agent, needsPointer: true, needsAgents: true,
                    required: true),

                new FtueStep(FtueGoal.OpenMenu,
                    "One button opens everything",
                    touch
                        ? "Tap the <b>button</b>, bottom-left. Load, Save, Inventory and Shop all live "
                            + "behind it, and a second tap closes them again."
                        : "Press <b>TAB</b>, bottom-left. Load, Save, Inventory and Shop all live behind "
                            + "it, and the same key closes them again.",
                    FtueIcon.Menu, FtueTarget.MenuButton),

                new FtueStep(FtueGoal.OpenInventory,
                    "Your inventory",
                    "Open <b>INVENTORY</b>. Everything you own is in there, sorted into floors, walls, "
                        + "tables and more.",
                    FtueIcon.Inventory, FtueTarget.Inventory),

                new FtueStep(FtueGoal.PlacePiece,
                    "Place something",
                    "<b>Drag</b> a piece onto the floor, then <b>click</b> to put it down. The wheel "
                        + "turns it; right-click cancels.",
                    FtueIcon.Place, FtueTarget.None, needsPointer: true),

                // Required: the tour waits for the player to save through SAVE WORLD rather than
                // letting NEXT walk past. The save the world makes by itself on leaving Edit does
                // not count - the tour closing the drawer to point at the menu set that off, and it
                // used to finish this step before the player had done anything.
                new FtueStep(FtueGoal.SaveWorld,
                    "Save your floor",
                    "Choose <b>SAVE WORLD</b> and give it a name. START in the Lobby brings you back "
                        + "to it.",
                    FtueIcon.Save, FtueTarget.SaveWorld, required: true),

                // Skipped for now (needsShop): it is not settled that there will be a shop. Kept so it
                // comes back by passing hasShop to For. NEXT rather than START BUILDING, since the
                // card below now ends the tour. Waits for the inventory to shut: the player is
                // usually still in it after saving, and the shop is a different screen.
                new FtueStep(FtueGoal.None,
                    "Packs add new pieces",
                    "Open <b>SHOP</b> to see what is in season. Strawberry is ready now; Grape and "
                        + "Coffee are on the way.",
                    FtueIcon.Shop, FtueTarget.Shop, FtueAccent.Shop,
                    "OPEN SHOP", "NEXT", secondaryOpensShop: true, needsInventoryClosed: true,
                    needsShop: true),

                // The last card, and the only one with no SKIP TOUR: there is nothing left to skip.
                // Waits for the inventory to shut - a well-done over the drawer the player is
                // working in would be in the way.
                new FtueStep(FtueGoal.Finish,
                    "You're all set",
                    touch
                        ? "Your floor is saved, and everything else lives behind the <b>button</b>, "
                            + "bottom-left. The rest is yours to build."
                        : "Your floor is saved, and everything else lives behind <b>TAB</b>. The rest "
                            + "is yours to build.",
                    FtueIcon.Inventory, FtueTarget.None,
                    secondary: null, primary: "START BUILDING", needsInventoryClosed: true),
            };
        }

        /// <summary>
        /// The steps this player will actually see: without the pointer steps on a touchscreen,
        /// without the agent step when there is no manager or no agent to meet, and without the
        /// shop step unless <paramref name="hasShop"/>.
        /// </summary>
        public static IReadOnlyList<FtueStep> For(bool touch, bool hasAgents, bool hasShop = false)
        {
            var kept = new List<FtueStep>();

            foreach (var step in All(touch))
            {
                if ((touch && step.NeedsPointer) || (!hasAgents && step.NeedsAgents) || (!hasShop && step.NeedsShop))
                {
                    continue;
                }

                kept.Add(step);
            }

            return kept;
        }
    }
}
