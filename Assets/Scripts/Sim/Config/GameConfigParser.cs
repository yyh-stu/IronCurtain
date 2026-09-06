using Newtonsoft.Json;

namespace IronCurtain.Sim
{
    public static class GameConfigParser
    {
        public static GameConfig FromJson(string json)
        {
            return JsonConvert.DeserializeObject<GameConfig>(json);
        }
    }
}
