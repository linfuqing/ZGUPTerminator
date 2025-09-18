using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

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
    [SerializeField]
    internal Energies _energies;

    public const string NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_DIAMOND = "UserEnergiesBuyTimesByDiamond";
    public const string NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_AD = "UserEnergiesBuyTimesByDiamond";
    
    public IEnumerator QueryEnergies(uint userID, Action<IUserData.Energies> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.Energies energies;
        energies.energyPerTime = _energies.energyPerTime;
        energies.diamondPerTime = _energies.diamondPerTime;
        energies.buyTimesByDiamond = _energies.buyTimesByDiamond -
                                     new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_DIAMOND), __Parse).ToDay();
        energies.buyTimesByAd = _energies.buyTimesByAd -
                                new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_AD), __Parse).ToDay();
        
        onComplete(energies);
    }

    public IEnumerator BuyEnergies(uint userID, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();
        
        int diamond = UserDataMain.diamond;
        if (diamond >= _energies.diamondPerTime)
        {
            int buyTimesByDiamond = new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_DIAMOND), __Parse).ToDay();
            if (buyTimesByDiamond < _energies.buyTimesByDiamond)
            {
                UserDataMain.diamond -= _energies.diamondPerTime;

                __ApplyEnergy(-_energies.energyPerTime);
                
                PlayerPrefs.SetString(NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_DIAMOND, new Active<int>(++buyTimesByDiamond).ToString());
                
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
        public UserProduct.Type productType;
        public UserCurrencyType currencyType;
        public int price;
        public int minChapter;
        public int maxChapter;
        public float chance;
        public UserRewardData[] rewards;
        
        #if UNITY_EDITOR
        [CSVField]
        public string 商品名称
        {
            set => name = value;
        }
        
        [CSVField]
        public int 商品类型
        {
            set => productType = (UserProduct.Type)value;
        }
        
        [CSVField]
        public int 商品货币类型
        {
            set => currencyType = (UserCurrencyType)value;
        }
        
        [CSVField]
        public int 商品价钱
        {
            set => price = value;
        }
        
        [CSVField]
        public int 商品最小章节
        {
            set => minChapter = value;
        }

        [CSVField]
        public int 商品最大章节
        {
            set => maxChapter = value;
        }

        [CSVField]
        public float 商品概率
        {
            set => chance = value;
        }
        
        [CSVField]
        public string 商品奖励
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    rewards = null;
                    
                    return;
                }
                
                var parameters = value.Split('/');
                int numParameters = parameters.Length;
                rewards = new UserRewardData[numParameters];
                for(int i = 0; i < numParameters; ++i)
                    rewards[i] = UserRewardData.Parse(parameters[i]);
            }
        }
        #endif
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

        public override string ToString()
        {
            return $"{value}{UserData.SEPARATOR}{bits}";
        }
    }
    
    [SerializeField]
    internal Product[] _products;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_products", guidIndex = -1, nameIndex = 0)] 
    internal string _productsPath;
#endif

    public const string NAME_SPACE_USER_PRODUCT_SEED = "UserProductSeed";

    public IEnumerator QueryProducts(uint userID, Action<Memory<UserProduct>> onComplete)
    {
        yield return __CreateEnumerator();

        var seed = new Active<ProductSeed>(PlayerPrefs.GetString(NAME_SPACE_USER_PRODUCT_SEED), ProductSeed.Parse).ToDay();
        if (seed.value == 0)
        {
            seed.value = DateTimeUtility.GetSeconds();
            
            PlayerPrefs.SetString(NAME_SPACE_USER_PRODUCT_SEED, new Active<ProductSeed>(seed).ToString());
        }
        
        List<UserProduct> results = null;
        UserProduct userProduct;
        Product product;
        var random = new Unity.Mathematics.Random(seed.value);
        var randomSelector = new RandomSelector(ref random);
        int numProducts = _products.Length, bitIndex = 0, chapter = UserData.chapter;
        for (int i = 0; i < numProducts; ++i)
        {
            product = _products[i];

            if(product.minChapter > chapter ||
               product.minChapter < product.maxChapter && product.maxChapter <= chapter || 
               !randomSelector.Select(ref random, product.chance))
                continue;

            userProduct.name = product.name;
            userProduct.id = __ToID(bitIndex);
            userProduct.flag = (seed.bits & (1 << bitIndex)) == 0 ? 0 : UserProduct.Flag.Collected;
            userProduct.productType = product.productType;
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
        yield return __CreateEnumerator();
        
        var seed = new Active<ProductSeed>(PlayerPrefs.GetString(NAME_SPACE_USER_PRODUCT_SEED), ProductSeed.Parse).ToDay();
        if (seed.value == 0)
        {
            seed.value = DateTimeUtility.GetSeconds();
            
            PlayerPrefs.SetString(NAME_SPACE_USER_PRODUCT_SEED, new Active<ProductSeed>(seed).ToString());
        }
        
        int index = __ToIndex(productID);
        if ((seed.bits & (1 << index)) == 0)
        {
            Product product;
            var random = new Unity.Mathematics.Random(seed.value);
            var randomSelector = new RandomSelector(ref random);
            int numProducts = _products.Length, bitIndex = 0, chapter = UserData.chapter;
            bool result;
            for (int i = 0; i < numProducts; ++i)
            {
                product = _products[i];
                
                if(product.minChapter > chapter ||
                   product.minChapter < product.maxChapter && product.maxChapter <= chapter || 
                   !randomSelector.Select(ref random, product.chance))
                    continue;

                if (index == bitIndex)
                {
                    switch (product.currencyType)
                    {
                        case UserCurrencyType.Gold:
                            result = gold >= product.price;
                            if (result)
                                gold -= product.price;

                            break;
                        case UserCurrencyType.Diamond:
                            result = diamond >= product.price;
                            if (result)
                                diamond -= product.price;

                            break;
                        case UserCurrencyType.Ad:
                            result = AdvertisementData.Exchange(AdvertisementType.Product, product.name,
                                NAME_SPACE_USER_PRODUCT_AD);
                            break;
                        case UserCurrencyType.Free:
                            result = true;
                            break;
                        default:
                            result = false;
                            break;
                    }

                    if (result)
                    {
                        if(UserProduct.Type.Normal != product.productType)
                            seed.bits |= 1 << index;
                        
                        PlayerPrefs.SetString(NAME_SPACE_USER_PRODUCT_SEED, new Active<ProductSeed>(seed).ToString());

                        onComplete(__ApplyRewards(product.rewards).ToArray());

                        yield break;
                    }

                    break;
                }

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
