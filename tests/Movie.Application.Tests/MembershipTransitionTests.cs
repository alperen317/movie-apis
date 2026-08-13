using Movie.Domain.Lists;
using Shouldly;

namespace Movie.Application.Tests;

/// <summary>
/// The rule the prevent_list_member_tampering trigger enforced. It needs no
/// database, so it is tested where it lives.
/// </summary>
public sealed class MembershipTransitionTests
{
    [Theory]
    [InlineData(MemberStatus.Pending, MemberStatus.Accepted)]
    [InlineData(MemberStatus.Pending, MemberStatus.Declined)]
    public void An_invitation_can_be_answered(MemberStatus from, MemberStatus to) =>
        Membership(from).CanTransitionTo(to).ShouldBeTrue();

    [Fact]
    public void A_declined_invitation_can_be_sent_again() =>
        Membership(MemberStatus.Declined).CanTransitionTo(MemberStatus.Pending).ShouldBeTrue();

    [Theory]
    [InlineData(MemberStatus.Accepted, MemberStatus.Pending)]
    [InlineData(MemberStatus.Accepted, MemberStatus.Declined)]
    public void Joining_is_final(MemberStatus from, MemberStatus to) =>
        // Leaving is deleting the row. Allowing a way back would let a stale
        // client quietly undo a membership.
        Membership(from).CanTransitionTo(to).ShouldBeFalse();

    [Fact]
    public void A_declined_invitation_cannot_be_accepted_without_being_reissued() =>
        // The whole point of the guard: answering an invitation is a one-way
        // step, so nobody can grant themselves a membership they turned down.
        Membership(MemberStatus.Declined).CanTransitionTo(MemberStatus.Accepted).ShouldBeFalse();

    [Theory]
    [InlineData(MemberStatus.Pending)]
    [InlineData(MemberStatus.Accepted)]
    [InlineData(MemberStatus.Declined)]
    public void Standing_still_is_not_a_transition(MemberStatus status) =>
        Membership(status).CanTransitionTo(status).ShouldBeFalse();

    private static ListMember Membership(MemberStatus status) => new()
    {
        ListId = Guid.CreateVersion7(),
        UserId = Guid.CreateVersion7(),
        Status = status,
    };
}
