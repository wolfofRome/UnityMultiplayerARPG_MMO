﻿using System.Collections.Generic;
using UnityEngine;
using LiteNetLibManager;
using LiteNetLib.Utils;
using Cysharp.Threading.Tasks;

namespace MultiplayerARPG.MMO
{
    public class UIMmoCharacterList : UICharacterList
    {
        protected override void LoadCharacters()
        {
            eventOnNotAbleToCreateCharacter.Invoke();
            MMOClientInstance.Singleton.RequestCharacters(OnRequestedCharacters);
        }

        private async UniTaskVoid OnRequestedCharacters(ResponseHandlerData responseHandler, AckResponseCode responseCode, ResponseCharactersMessage response)
        {
            await UniTask.Yield();
            // Clear character list
            CacheCharacterSelectionManager.Clear();
            CacheCharacterList.HideAll();
            // Unable buttons
            buttonStart.gameObject.SetActive(false);
            buttonDelete.gameObject.SetActive(false);
            // Remove all models
            characterModelContainer.RemoveChildren();
            CharacterModelById.Clear();
            // Remove all cached data
            PlayerCharacterDataById.Clear();
            // Proceed response
            List<PlayerCharacterData> selectableCharacters = new List<PlayerCharacterData>();
            if (!responseCode.ShowUnhandledResponseMessageDialog(() =>
            {
                string errorMessage = string.Empty;
                switch (response.error)
                {
                    case ResponseCharactersMessage.Error.NotLoggedin:
                        errorMessage = LanguageManager.GetText(UITextKeys.UI_ERROR_NOT_LOGGED_IN.ToString());
                        break;
                }
                UISceneGlobal.Singleton.ShowMessageDialog(LanguageManager.GetText(UITextKeys.UI_LABEL_ERROR.ToString()), errorMessage);
            }))
            {
                // Success, so set selectable characters by response's data
                selectableCharacters = response.characters;
            }
            // Show list of created characters
            for (int i = selectableCharacters.Count - 1; i >= 0; --i)
            {
                PlayerCharacterData selectableCharacter = selectableCharacters[i];
                if (selectableCharacter == null ||
                    !GameInstance.PlayerCharacterEntities.ContainsKey(selectableCharacter.EntityId) ||
                    !GameInstance.PlayerCharacters.ContainsKey(selectableCharacter.DataId))
                {
                    // If invalid entity id or data id, remove from selectable character list
                    selectableCharacters.RemoveAt(i);
                }
            }

            if (GameInstance.Singleton.maxCharacterSaves > 0 &&
                selectableCharacters.Count >= GameInstance.Singleton.maxCharacterSaves)
                eventOnNotAbleToCreateCharacter.Invoke();
            else
                eventOnAbleToCreateCharacter.Invoke();

            // Clear selected character data, will select first in list if available
            selectedPlayerCharacterData = null;

            // Generate list entry by saved characters
            if (selectableCharacters.Count > 0)
            {
                selectableCharacters.Sort(new PlayerCharacterDataLastUpdateComparer().Desc());
                CacheCharacterList.Generate(selectableCharacters, (index, characterData, ui) =>
                {
                    // Cache player character to dictionary, we will use it later
                    PlayerCharacterDataById[characterData.Id] = characterData;
                    // Setup UIs
                    UICharacter uiCharacter = ui.GetComponent<UICharacter>();
                    uiCharacter.Data = characterData;
                    // Select trigger when add first entry so deactivate all models is okay beacause first model will active
                    BaseCharacterModel characterModel = characterData.InstantiateModel(characterModelContainer);
                    if (characterModel != null)
                    {
                        CharacterModelById[characterData.Id] = characterModel;
                        characterModel.SetEquipWeapons(characterData.EquipWeapons);
                        characterModel.SetEquipItems(characterData.EquipItems);
                        characterModel.gameObject.SetActive(false);
                        CacheCharacterSelectionManager.Add(uiCharacter);
                    }
                });
            }
            else
            {
                eventOnNoCharacter.Invoke();
            }
        }

        protected override void OnSelectCharacter(IPlayerCharacterData playerCharacterData)
        {
            // Do nothing
        }

        public override void OnClickStart()
        {
            UICharacter selectedUI = CacheCharacterSelectionManager.SelectedUI;
            if (selectedUI == null)
            {
                UISceneGlobal.Singleton.ShowMessageDialog(LanguageManager.GetText(UITextKeys.UI_LABEL_ERROR.ToString()), LanguageManager.GetText(UITextKeys.UI_ERROR_NO_CHOSEN_CHARACTER_TO_START.ToString()));
                Debug.LogWarning("Cannot start game, No chosen character");
                return;
            }
            // Load gameplay scene, we're going to manage maps in gameplay scene later
            // So we can add gameplay UI just once in gameplay scene
            IPlayerCharacterData playerCharacter = selectedUI.Data as IPlayerCharacterData;
            MMOClientInstance.Singleton.RequestSelectCharacter(playerCharacter.Id, OnRequestedSelectCharacter);
        }

        private async UniTaskVoid OnRequestedSelectCharacter(ResponseHandlerData responseHandler, AckResponseCode responseCode, ResponseSelectCharacterMessage response)
        {
            await UniTask.Yield();
            if (responseCode.ShowUnhandledResponseMessageDialog(() =>
            {
                string errorMessage = string.Empty;
                switch (response.error)
                {
                    case ResponseSelectCharacterMessage.Error.NotLoggedin:
                        errorMessage = LanguageManager.GetText(UITextKeys.UI_ERROR_NOT_LOGGED_IN.ToString());
                        break;
                    case ResponseSelectCharacterMessage.Error.AlreadySelectCharacter:
                        errorMessage = LanguageManager.GetText(UITextKeys.UI_ERROR_ALREADY_SELECT_CHARACTER.ToString());
                        break;
                    case ResponseSelectCharacterMessage.Error.InvalidCharacterData:
                        errorMessage = LanguageManager.GetText(UITextKeys.UI_ERROR_INVALID_CHARACTER_DATA.ToString());
                        break;
                    case ResponseSelectCharacterMessage.Error.MapNotReady:
                        errorMessage = LanguageManager.GetText(UITextKeys.UI_ERROR_MAP_SERVER_NOT_READY.ToString());
                        break;
                }
                UISceneGlobal.Singleton.ShowMessageDialog(LanguageManager.GetText(UITextKeys.UI_LABEL_ERROR.ToString()), errorMessage);
            })) return;
            MMOClientInstance.Singleton.StartMapClient(response.sceneName, response.networkAddress, response.networkPort);
        }

        public override void OnClickDelete()
        {
            UICharacter selectedUI = CacheCharacterSelectionManager.SelectedUI;
            if (selectedUI == null)
            {
                UISceneGlobal.Singleton.ShowMessageDialog(LanguageManager.GetText(UITextKeys.UI_LABEL_ERROR.ToString()), LanguageManager.GetText(UITextKeys.UI_ERROR_NO_CHOSEN_CHARACTER_TO_DELETE.ToString()));
                Debug.LogWarning("Cannot delete character, No chosen character");
                return;
            }

            IPlayerCharacterData playerCharacter = selectedUI.Data as IPlayerCharacterData;
            MMOClientInstance.Singleton.RequestDeleteCharacter(playerCharacter.Id, OnRequestedDeleteCharacter);
        }

        private async UniTaskVoid OnRequestedDeleteCharacter(ResponseHandlerData responseHandler, AckResponseCode responseCode, ResponseDeleteCharacterMessage response)
        {
            await UniTask.Yield();
            if (responseCode.ShowUnhandledResponseMessageDialog(() =>
            {
                string errorMessage = string.Empty;
                switch (response.error)
                {
                    case ResponseDeleteCharacterMessage.Error.NotLoggedin:
                        errorMessage = LanguageManager.GetText(UITextKeys.UI_ERROR_NOT_LOGGED_IN.ToString());
                        break;
                }
                UISceneGlobal.Singleton.ShowMessageDialog(LanguageManager.GetText(UITextKeys.UI_LABEL_ERROR.ToString()), errorMessage);
            })) return;
            // Reload characters
            LoadCharacters();
        }
    }
}
