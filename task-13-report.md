# Task 13 report
Built: App/Services/LooseCreation (NewRecipeAsync/NewIngredientAsync -> path|null). Kitchen default, picker try/catch, empty-id guard, error cards, "\n" join, writes via LooseWorkspace (AtomicFile). Landing + Explorer copies deleted. A5: Core LoadedRecipe.Empty(id,name), used in service + Explorer.AddRecipe.
Explorer now opens new loose recipe (new optional ctor params looseRecipeFactory, recents; wired in ServiceRegistration) and records recent. Ingredient: both open loose editor.
Deviations: Problems.Join not in repo -> inline "\n". VMs build LooseCreation internally (no ctor churn, 40+ call sites).
Tests: LooseCreationTests 20 (5 facts x Landing/Explorer x Recipe/Ingredient). Written after impl (no red run); probes instead.
Probes: kitchen branch off -> 4 fail; picker catch off -> 4 fail; empty-id guard off -> 1 fail (others refused downstream by Validator / Explorer pre-check); Landing skip OpenLooseRecipe -> 1 fail. All reverted.
Final: build 0 errors; App.Tests targeted + 5 sweeps + Landing*/Explorer* = 170 pass.
