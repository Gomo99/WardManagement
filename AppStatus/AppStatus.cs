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
        Delivered,
        Dispensed
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
        Fulfilled,
        Cancelled
    }

   



}
