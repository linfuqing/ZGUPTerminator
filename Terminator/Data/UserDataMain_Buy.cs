using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class UserDataMain
{
    [Serializable]
    internal struct Energies
    {
        public int energyPerTime;
        public int diamondPerTime;
        public int buyTimesByDiamond;
        public int buyTimesByAd;
    }

    [Header("Buy")] 
    internal Energies _energies;

    public const string NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_DIAMOND = "UserEnergiesBuyTimesByDiamond";
    public const string NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_AD = "UserEnergiesBuyTimesByDiamond";
    
    public IEnumerator QueryEnergies(uint userID, Action<IUserData.Energies> onComplete)
    {
        yield return null;

        IUserData.Energies energies;
        energies.energyPerTime = _energies.energyPerTime;
        energies.diamondPerTime = _energies.diamondPerTime;
        energies.buyTimesByDiamond = _energies.buyTimesByDiamond -
                                     PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_DIAMOND);
        energies.buyTimesByAd = _energies.buyTimesByAd -
                                PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_AD);
        
        onComplete(energies);
    }

    public IEnumerator BuyEnergies(uint userID, Action<bool> onComplete)
    {
        yield return null;
        
        int diamond = UserDataMain.diamond;
        if (diamond >= _energies.diamondPerTime)
        {
            int buyTimesByDiamond = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_DIAMOND);
            if (buyTimesByDiamond < _energies.buyTimesByDiamond)
            {
                UserDataMain.diamond -= diamond;

                __ApplyEnergy(-_energies.energyPerTime);
                
                PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_DIAMOND, ++buyTimesByDiamond);
                
                onComplete(true);
                
                yield break;
            }
        }

        onComplete(false);
    }

    [Serializable]
    internal struct Product
    {
        public string name;
        public UserCurrencyType currencyType;
        public int price;
        public float chance;
        public UserRewardData[] rewards;
    }
    
    [SerializeField]
    internal Product[] _products;

    public IEnumerator QueryProducts(uint userID, Action<Memory<UserProduct>> onComplete)
    {
        yield return null;

        
    }

    public IEnumerator BuyProduct(uint userID, uint productID, Action<Memory<UserReward>> onComplete)
    {
        yield return null;
    }
}

public partial class UserData
{
    public IEnumerator QueryEnergies(uint userID, Action<IUserData.Energies> onComplete)
    {
        return UserDataMain.instance.QueryEnergies(userID, onComplete);
    }

    public IEnumerator BuyEnergies(uint userID, Action<bool> onComplete)
    {
        return UserDataMain.instance.BuyEnergies(userID, onComplete);
    }
    
    public IEnumerator QueryProducts(uint userID, Action<Memory<UserProduct>> onComplete)
    {
        return UserDataMain.instance.QueryProducts(userID, onComplete);
    }

    public IEnumerator BuyProduct(uint userID, uint productID, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.BuyProduct(userID, productID, onComplete);
    }
}
