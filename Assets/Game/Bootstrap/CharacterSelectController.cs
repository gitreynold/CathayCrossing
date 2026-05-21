using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CathayCrossing.Characters;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// Drives the CharacterSelect scene. Discovers every
    /// <see cref="CharacterDefinition"/> in <c>Resources/Characters/</c>, spawns
    /// one UI Button per character into the assigned container, and on click
    /// writes the chosen id to PlayerPrefs and loads <c>officeSceneName</c>.
    ///
    /// The spawner then reads the same PlayerPrefs key during OfficeScene boot.
    /// No DontDestroyOnLoad / singleton — selection lives in PlayerPrefs so it
    /// also survives a full app restart.
    /// </summary>
    public class CharacterSelectController : MonoBehaviour
    {
        [Tooltip("Parent transform that receives the per-character buttons. " +
                 "Usually a VerticalLayoutGroup under a UI Canvas.")]
        public Transform buttonContainer;

        [Tooltip("Template button cloned for each CharacterDefinition. Should " +
                 "be a disabled UI Button with a Text/TMP_Text child.")]
        public Button buttonTemplate;

        [Tooltip("Scene loaded after the player picks a character.")]
        public string officeSceneName = "OfficeScene";

        void Start()
        {
            if (buttonContainer == null || buttonTemplate == null)
            {
                Debug.LogError("[CharacterSelectController] buttonContainer or buttonTemplate not assigned.");
                return;
            }

            var characters = Resources.LoadAll<CharacterDefinition>("Characters");
            if (characters == null || characters.Length == 0)
            {
                Debug.LogError("[CharacterSelectController] No CharacterDefinitions in Resources/Characters/. " +
                               "Run Tools › CathayCrossing › Setup All Characters first.");
                return;
            }

            // Deterministic order so Default lands first on most runs.
            System.Array.Sort(characters, (a, b) => string.CompareOrdinal(a.name, b.name));

            buttonTemplate.gameObject.SetActive(false);

            foreach (var def in characters)
            {
                if (def == null) continue;
                var btn = Instantiate(buttonTemplate, buttonContainer);
                btn.gameObject.SetActive(true);
                btn.name = "Button_" + def.id;

                // Label: try TMP first (project ships TextMeshPro), then legacy Text.
                var tmp = btn.GetComponentInChildren<TMPro.TMP_Text>(includeInactive: true);
                if (tmp != null) tmp.text = def.displayName;
                else
                {
                    var legacy = btn.GetComponentInChildren<Text>(includeInactive: true);
                    if (legacy != null) legacy.text = def.displayName;
                }

                string idCopy = def.id; // capture for closure
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Pick(idCopy));
            }
        }

        void Pick(string id)
        {
            PlayerPrefs.SetString(OfficePlayerSpawner.ActiveCharacterPrefsKey, id);
            PlayerPrefs.Save();
            Debug.Log($"[CharacterSelectController] Picked '{id}'. Loading '{officeSceneName}'.");
            SceneManager.LoadScene(officeSceneName);
        }
    }
}
