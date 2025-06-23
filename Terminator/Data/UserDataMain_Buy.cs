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

    private struct ProductSeed
    {
        public uint value;

        public int bits;
        
        public static ProductSeed Parse(Memory<string> value)
        {
            ProductSeed result;
            result.value = uint.Parse(value.Span[0]);
            result.bits = int.Parse(value.Span[1]);

            return result;
        }
    }
    
    [SerializeField]
    internal Product[] _products;

    public const string NAME_SPACE_USER_PRODUCT_SEED = "UserProductSeed";

    public IEnumerator QueryProducts(uint userID, Action<Memory<UserProduct>> onComplete)
    {
        yield return null;

        var seed = new Active<ProductSeed>(PlayerPrefs.GetString(NAME_SPACE_USER_PRODUCT_SEED), ProductSeed.Parse).ToDay();
        if (seed.value == 0)
        {
            seed.value = (uint)((DateTime.UtcNow.Ticks - Utc1970.Ticks) / TimeSpan.TicksPerSecond);
            
            PlayerPrefs.SetString(NAME_SPACE_USER_PRODUCT_SEED, new Active<ProductSeed>(seed).ToString());
        }
        
        List<UserProduct> results = null;
        UserProduct userProduct;
        Product product;
        var random = new Unity.Mathematics.Random(seed.value);
        float randomValue = random.NextFloat(), chance = 0.0f;
        int numProducts = _products.Length, bitIndex = 0;
        bool isSelected = false;
        for (int i = 0; i < numProducts; ++i)
        {
            product = _products[i];

            chance += product.chance;
            if (chance > 1.0f)
            {
                chance -= 1.0f;

                isSelected = false;
            }
            
            if(isSelected || chance < randomValue)
                continue;

            isSelected = true;
            
            userProduct.name = product.name;
            userProduct.id = __ToID(bitIndex);
            userProduct.flag = (seed.bits & (1 << bitIndex)) == 0 ? 0 : UserProduct.Flag.Collected;
            userProduct.currencyType = product.currencyType;
            userProduct.price = product.price;
            userProduct.rewards = product.rewards;
            
            if(results == null)
                results = new List<UserProduct>();
            
            results.Add(userProduct);

            ++bitIndex;
        }
        
        onComplete(results.ToArray());
    }

    public const string NAME_SPACE_USER_PRODUCT_AD = "UserProductAd";

    public IEnumerator BuyProduct(uint userID, uint productID, Action<Memory<UserReward>> onComplete)
    {
        yield return null;
        
        var seed = new Active<ProductSeed>(PlayerPrefs.GetString(NAME_SPACE_USER_PRODUCT_SEED), ProductSeed.Parse).ToDay();
        int index = __ToIndex(productID);
        if ((seed.bits & (1 << index)) == 0)
        {
            Product product;
            var random = new Unity.Mathematics.Random(seed.value);
            float randomValue = random.NextFloat(), chance = 0.0f;
            int numProducts = _products.Length, bitIndex = 0;
            bool isSelected = false;
            for (int i = 0; i < numProducts; ++i)
            {
                product = _products[i];

                chance += product.chance;
                if (chance > 1.0f)
                {
                    chance -= 1.0f;

                    isSelected = false;
                }

                if (isSelected || chance < randomValue)
                    continue;

                if (index == bitIndex)
                {
                    isSelected = false;

                    switch (product.currencyType)
                    {
                        case UserCurrencyType.Gold:
                            isSelected = gold >= product.price;
                            if (isSelected)
                                gold -= product.price;

                            break;
                        case UserCurrencyType.Diamond:
                            isSelected = diamond >= product.price;
                            if (isSelected)
                                diamond -= product.price;

                            break;
                        case UserCurrencyType.Ad:
                            isSelected = AdvertisementData.Exchange(AdvertisementType.Product, product.name,
                                NAME_SPACE_USER_PRODUCT_AD);
                            break;
                        case UserCurrencyType.Free:
                            isSelected = true;
                            break;
                        default:
                            isSelected = false;
                            break;
                    }

                    if (isSelected)
                    {
                        seed.bits |= 1 << index;
                        
                        PlayerPrefs.SetString(NAME_SPACE_USER_PRODUCT_SEED, new Active<ProductSeed>(seed).ToString());

                        onComplete(__ApplyRewards(product.rewards).ToArray());

                        yield break;
                    }

                    break;
                }

                isSelected = true;

                ++bitIndex;
            }
        }

        onComplete(null);
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
