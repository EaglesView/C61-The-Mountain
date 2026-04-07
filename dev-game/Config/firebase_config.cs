namespace Config;

public static class FirebaseConfig
{
    public static string ApiKey    => Env.Get("FIREBASE_API_KEY");
    public static string ProjectId => Env.Get("FIREBASE_PROJECT_ID");
}
