// USharpInputHelper.cpp
#include "USharpInputHelper.h"
#include "InputMappingContext.h"
#include "InputAction.h"
#include "InputModifiers.h"

void USharpInputHelper::MapKey(
    UInputMappingContext* IMC,
    UInputAction* Action,
    FKey Key)
{
    if (!IMC || !Action) return;
    IMC->MapKey(Action, Key);
}

void USharpInputHelper::MapKeyWithModifiers(
    UInputMappingContext* IMC,
    UInputAction* Action,
    FKey Key,
    const TArray<UInputModifier*>& Modifiers)
{
    if (!IMC || !Action) return;

    FEnhancedActionKeyMapping& NewMapping = IMC->MapKey(Action, Key);
    for (UInputModifier* Mod : Modifiers)
    {
        if (Mod)
            NewMapping.Modifiers.Add(Mod);
    }
}
