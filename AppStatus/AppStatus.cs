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
        CONSUMABLESMANAGER
    }

    public enum ScriptStatus
    {
        New,
        ForwardedToPharmacy,
        Delivered
    }




    public enum GenderType 
    
    { 
        Female, Male
    
    
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
        Cancelled
    }

   



}
