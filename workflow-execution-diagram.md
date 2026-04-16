# Elsa Workflow Execution Flow - Order ORD-2024-005

## System Architecture & Data Flow

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                          ELSA WORKFLOW EXECUTION FLOW                          │
└─────────────────────────────────────────────────────────────────────────────────┘

📝 INPUT REQUEST (5 items → 3 stores)
┌─────────────────────────────────────────────────────────────────────────────────┐
│ FulfilmentId: 00145678-1234-5678-9abc-123456789012                             │
│ OrderNo: ORD-2024-005                                                           │
│ Items: WIDGET-RED(2), WIDGET-BLUE(1), GADGET-SMALL(5), TOOL-HAMMER(1), PART-ENGINE(3) │
└─────────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────────┐
│                            🗄️ MONGO REPOSITORY                                  │
│ [MongoRepository] Inserting new aggregate (Version: 0)                         │
│ ✅ Inserted aggregate 00145678... (Version: 0)                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────────┐
│                          🔄 FULFILMENT WORKFLOW                                │
│ Allocation Logic: Split 5 items across 3 stores                                │
└─────────────────────────────────────────────────────────────────────────────────┘
                          ↓         ↓         ↓
    ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
    │  🏪 STORE-DEFAULT    │  🏪 STORE-EAST      │  🏪 STORE-WEST      │
    │  📦 2 lines         │  📦 2 lines         │  📦 1 line          │
    │  • WIDGET-RED (2)   │  • WIDGET-BLUE (1) │  • GADGET-SMALL (5) │
    │  • TOOL-HAMMER (1)  │  • PART-ENGINE (3) │                     │
    │  ⏱️ 22s delay        │  ⏱️ 24s delay       │  ⏱️ 12s delay       │
    └─────────────────┘     └─────────────────┘     └─────────────────┘
                          ↓         ↓         ↓
          ⚡ FIRE-AND-FORGET PREPARATION WORKFLOWS (3 CONCURRENT)
                          ↓         ↓         ↓
    ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
    │ 🔄 PrepWorkflow  │     │ 🔄 PrepWorkflow  │     │ 🔄 PrepWorkflow  │
    │ Store variables  │     │ Store variables  │     │ Store variables  │
    │ Start 22s delay  │     │ Start 24s delay  │     │ Start 12s delay  │
    └─────────────────┘     └─────────────────┘     └─────────────────┘

⏰ EXECUTION TIMELINE & AGGREGATE STATE CHANGES
════════════════════════════════════════════════════════════════════════════════

t=0s   ┌─────────────────────────────────────────────────────────────────┐
       │ 🚀 All 3 workflows start delays simultaneously                  │
       │ 📊 Aggregate State: Version 0, Containers: 0                   │
       └─────────────────────────────────────────────────────────────────┘

t=12s  ┌─────────────────────────────────────────────────────────────────┐
       │ 🏪 STORE-WEST completes first (shortest delay)                  │ 
       │ 📦 Generates: 1 container (CONT-01 Tote → GADGET-SMALL×5)      │
       │ 🔄 Sub-workflow updates aggregate                               │
       │ 📊 Aggregate State: Version 0→1, Containers: 1                 │
       └─────────────────────────────────────────────────────────────────┘
                                    ↓
       ┌─────────────────────────────────────────────────────────────────┐
       │              📡 HTTP POST /api/preparation-outcome              │
       │ [PreparationOutcomeSubWorkflow] Correlation-based routing       │
       │ [UpdateAggregateWithContainers] Optimistic concurrency         │
       │ ✅ 200 OK (189ms)                                               │
       └─────────────────────────────────────────────────────────────────┘

t=22s  ┌─────────────────────────────────────────────────────────────────┐
       │ 🏪 STORE-DEFAULT completes second                               │
       │ 📦 Generates: 2 containers (CONT-01 Tote → WIDGET-RED×2,       │
       │                            CONT-02 Tote → TOOL-HAMMER×1)       │
       │ 🔄 Sub-workflow updates aggregate                               │
       │ 📊 Aggregate State: Version 1→2, Containers: 1+2=3             │
       └─────────────────────────────────────────────────────────────────┘
                                    ↓
       ┌─────────────────────────────────────────────────────────────────┐
       │              📡 HTTP POST /api/preparation-outcome              │
       │ [PreparationOutcomeSubWorkflow] Correlation-based routing       │
       │ [UpdateAggregateWithContainers] Optimistic concurrency         │
       │ ✅ 200 OK (23ms) - No conflicts!                                │
       └─────────────────────────────────────────────────────────────────┘

t=24s  ┌─────────────────────────────────────────────────────────────────┐
       │ 🏪 STORE-EAST completes last                                    │
       │ 📦 Generates: 2 containers (CONT-01 Bag → WIDGET-BLUE×1,       │
       │                            CONT-02 Bag → PART-ENGINE×3)        │
       │ 🔄 Sub-workflow updates aggregate                               │
       │ 📊 Aggregate State: Version 2→3, Containers: 3+2=5             │
       └─────────────────────────────────────────────────────────────────┘
                                    ↓
       ┌─────────────────────────────────────────────────────────────────┐
       │              📡 HTTP POST /api/preparation-outcome              │
       │ [PreparationOutcomeSubWorkflow] Correlation-based routing       │
       │ [UpdateAggregateWithContainers] Optimistic concurrency         │
       │ ✅ 200 OK (19ms) - Perfect concurrency management!             │
       └─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────────┐
│                           🎯 FINAL RESULT                                      │
│ ✅ Order ORD-2024-005 Complete                                                  │
│ 📊 Aggregate Version: 3 (perfect version progression 0→1→2→3)                   │
│ 📦 Total Containers: 5                                                          │
│ 🏪 STORE-WEST:    1 Tote  (GADGET-SMALL×5)                                      │
│ 🏪 STORE-DEFAULT: 2 Totes (WIDGET-RED×2, TOOL-HAMMER×1)                        │  
│ 🏪 STORE-EAST:    2 Bags  (WIDGET-BLUE×1, PART-ENGINE×3)                       │
│ 🚫 Zero Concurrency Conflicts - Retry logic not needed!                        │
│ ⚡ Sub-workflow Architecture: Perfect correlation-based routing                 │
│ 💾 MongoDB Persistence: Optimistic concurrency with version management        │
└─────────────────────────────────────────────────────────────────────────────────┘

KEY ARCHITECTURAL PATTERNS DEMONSTRATED:
═══════════════════════════════════════════════════════════════════════════════════
🔄 Fire-and-Forget Workflows    → Direct runtime calls, no HTTP timeouts
⏱️ Elsa Delay Activities        → Proper suspension/resumption with state persistence  
🎯 Correlation-based Routing    → Sub-workflows triggered by outcome correlation IDs
🔒 Optimistic Concurrency      → Version-based conflict resolution with retry logic
📊 Aggregate State Management   → Domain-driven design with MongoDB persistence
🚀 Concurrent Execution         → 3 parallel preparation workflows, sequential updates
```