namespace Nodo.Quotes.Domain;

/// <summary>Domain service for BR-07: the plan's discount applies only while the membership is active.</summary>
public static class MembershipDiscountPolicy
{
    public static int Effective(bool isActiveMember, int planDiscountPercentage) =>
        isActiveMember ? planDiscountPercentage : 0;
}
