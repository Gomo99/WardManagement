namespace WARDMANAGEMENTSYSTEM.AppStatus
{
    public enum UserRole
    {
        ADMINISTRATOR,
       WARDADMIN,
       PATIENT,
        NURSE,
        NURSINGSISTER,
        DOCTOR,
        SCRIPTMANAGER,
        CONSUMABLESMANAGER,
        PHARMACIST,
        PORTER,
        SOCIALWORKER,
        SUPPLIER
    }

    public enum ScriptStatus
    {
        New,
        ForwardedToPharmacy,
        Dispensed,
        Delivered,
        Rejected   // <-- add this
    }

    public enum FollowUpType
    {
        PhoneCall,
        HomeVisit
    }

    public enum FollowUpStatus
    {
        Pending,
        Completed,
        Cancelled
    }


    public enum MeetingStatus
    {
        Scheduled,
        Completed,
        Cancelled
    }

    public enum DischargePlanStatus
    {
        Pending,
        InProgress,
        ReadyForReview,
        Approved,
        Implemented
    }

    public enum ReferralOutcome
    {
        Pending,
        Accepted,
        Declined,
        Completed
    }


    public enum FollowUpRequestStatus { Pending, Scheduled, Cancelled }


    public enum GenderType 
    
    { 
        Female, Male
    
    
    }

    public enum ScreeningType
    {
        Frailty,
        FallsRisk,
        CarerStressIndex
    }


    public enum Status
    {
        Active,
        Inactive
    }


    public enum OrderStatus
    {
        Ordered,
        PartiallyComplete,
        Complete,
        Fulfilled,
        Cancelled,
        PartiallyFulfilled,
        Rejected,
    }

   



}
