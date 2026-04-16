using Elsa.Basic.Flow.Scenarios.ParallelSubflows.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;

namespace Elsa.Basic.Flow.Scenarios.ParallelSubflows;

/// <summary>
/// Two sub-workflows executing in parallel via Fork WaitAll.
/// Sub-workflow 1 (Person)  — collects first name and surname.
/// Sub-workflow 2 (Address) — collects street and suburb.
/// The main workflow only proceeds once both branches are complete.
/// </summary>
public class ParallelDetailsWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var firstName = builder.WithVariable<string>();
        var surname   = builder.WithVariable<string>();
        var street    = builder.WithVariable<string>();
        var suburb    = builder.WithVariable<string>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new WriteLine("=== Parallel Details Scenario ==="),
                new WriteLine("Two sub-workflows running in parallel — answer each prompt as it appears."),
                new Fork
                {
                    JoinMode = ForkJoinMode.WaitAll,
                    Branches =
                    [
                        // ── Sub-workflow 1: Person ────────────────────────────
                        new Sequence
                        {
                            Activities =
                            [
                                new ConsolePrompt { Prompt = "[Person]  First name : ", Result = new(firstName) },
                                new ConsolePrompt { Prompt = "[Person]  Surname    : ", Result = new(surname) }
                            ]
                        },

                        // ── Sub-workflow 2: Address ───────────────────────────
                        new Sequence
                        {
                            Activities =
                            [
                                new ConsolePrompt { Prompt = "[Address] Street     : ", Result = new(street) },
                                new ConsolePrompt { Prompt = "[Address] Suburb     : ", Result = new(suburb) }
                            ]
                        }
                    ]
                },
                new WriteLine("─── Both sub-workflows completed ───────────────────"),
                new WriteLine(ctx => $"Name    : {firstName.Get(ctx)} {surname.Get(ctx)}"),
                new WriteLine(ctx => $"Address : {street.Get(ctx)}, {suburb.Get(ctx)}")
            ]
        };
    }
}
