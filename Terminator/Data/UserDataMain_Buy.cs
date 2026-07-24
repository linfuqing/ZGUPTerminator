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

    private const string NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_DIAMOND = "UserEnergiesBuyTimesByDiamond";
    private const string NAME_SPACE_USER_ENERGIES_BUY_TIMES_BY_AD = "UserEnergiesBuyTimesByAd";
    
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
        public int group;
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
        public int 商品分组
        {
            set => group = value;
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

    [SerializeField] 
    internal IUserData.Products.Refresh[] _productRefreshes;

    public IEnumerator QueryProducts(uint userID, Action<IUserData.Products> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.Products result;
        List<int> refreshCounts = null;
        List<IUserData.Products.Refresh> refreshes = null;
        if (_productRefreshes != null)
        {
            int i;
            foreach (var refresh in _productRefreshes)
            {
                refreshCounts ??= new List<int>();
                for (i = refreshCounts.Count; i <= (int)refresh.productType; ++i)
                    refreshCounts.Add(__GetRefreshProductCount((UserProduct.Type)i, refresh.group, out _));

                i = refreshCounts[(int)refresh.productType];
                if (i > 0)
                    refreshCounts[(int)refresh.productType] = i - 1;
                else
                {
                    refreshes ??= new List<IUserData.Products.Refresh>();
                    refreshes.Add(refresh);
                }
            }
        }
        
        result.refreshes = refreshes?.ToArray();

        HashSet<int> groups = null;
        foreach (var product in _products)
        {
            groups ??= new HashSet<int>();
            groups.Add(product.group);
        }

        List<UserProduct> products = null;
        foreach (var group in groups)
        {
            __CollectProducts(ref products, UserProduct.Type.Normal, group);
            __CollectProducts(ref products, UserProduct.Type.Day, group);
            __CollectProducts(ref products, UserProduct.Type.Week, group);
            __CollectProducts(ref products, UserProduct.Type.Month, group);
        }

        result.products = products.ToArray();
        
        onComplete(result);
    }

    public IEnumerator RefreshProducts(uint userID, int group, UserProduct.Type type, Action<Memory<UserProduct>> onComplete)
    {
        yield return __CreateEnumerator();

        if (!__RefreshProductSeed(type, group))
        {
            onComplete(default);
            
            yield break;
        }
        
        List<UserProduct> results = null;
        __CollectProducts(ref results, type, group);
        
        onComplete(results.ToArray());
    }

    public const string NAME_SPACE_USER_PRODUCT_AD = "UserProductAd";

    public IEnumerator BuyProduct(uint userID, uint productID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();
        
        int index = __ProductIDToBitIndex(productID, out int group, out var type);
        var seed = __GetProductSeed(type, group, out string key);
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
                
                if(product.productType != type || 
                   product.group != group || 
                   product.minChapter > chapter ||
                   product.minChapter < product.maxChapter && product.maxChapter <= chapter || 
                   !randomSelector.Select(ref random, product.chance))
                    continue;

                if (index == bitIndex)
                {
                    result = __ApplyProductType(product.name, product.currencyType, product.price);
                    if (result)
                    {
                        if(UserProduct.Type.Normal != product.productType)
                            seed.bits |= 1 << index;
                        
                        PlayerPrefs.SetString(key, new Active<ProductSeed>(seed).ToString());

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

    private bool __ApplyProductType(string name, UserCurrencyType currencyType, int price)
    {
        bool result;
        switch (currencyType)
        {
            case UserCurrencyType.Gold:
                result = gold >= price;
                if (result)
                    gold -= price;

                break;
            case UserCurrencyType.Diamond:
                result = diamond >= price;
                if (result)
                    diamond -= price;

                break;
            case UserCurrencyType.Ad:
                result = AdvertisementData.Exchange(AdvertisementType.Product, name,
                    NAME_SPACE_USER_PRODUCT_AD);
                break;
            case UserCurrencyType.Free:
                result = true;
                break;
            default:
                result = false;
                break;
        }

        return result;
    }

    private const string NAME_SPACE_USER_PRODUCT_SEED = "UserProductSeed";

    private string __GetProductTypeKey(UserProduct.Type type, int group)
    {
        return $"{NAME_SPACE_USER_PRODUCT_SEED}{type}{UserData.SEPARATOR}{group}";
    }

    private uint __ProductBitIndexToID(UserProduct.Type type, int group, int bitIndex)
    {
        return __ToID(bitIndex) | (uint)group << 24 | (uint)type << 30;
    }

    private int __ProductIDToBitIndex(uint id, out int group, out UserProduct.Type type)
    {
        int result = __ToIndex(id & 0xFFFFFF);
        group = (int)(id >> 24) & 0x3F;
        type = (UserProduct.Type)(id >> 30);
        return result;
    }

    private ProductSeed __GetProductSeed(UserProduct.Type type, int group, out string key)
    {
        key = __GetProductTypeKey(type, group);
        
        ProductSeed seed;
        switch (type)
        {
            case UserProduct.Type.Day:
                seed = new Active<ProductSeed>(PlayerPrefs.GetString(key), ProductSeed.Parse).ToDay();
                break;
            case  UserProduct.Type.Week:
                seed = new Active<ProductSeed>(PlayerPrefs.GetString(key), ProductSeed.Parse).ToWeek();
                break;
            case  UserProduct.Type.Month:
                seed = new Active<ProductSeed>(PlayerPrefs.GetString(key), ProductSeed.Parse).ToMonth();
                break;
            default:
                seed = new Active<ProductSeed>(PlayerPrefs.GetString(key), ProductSeed.Parse).value;
                break;
        }
        
        if (seed.value == 0)
        {
            seed.value = (uint)UnityEngine.Random.Range(0, int.MaxValue);
            
            PlayerPrefs.SetString(key, new Active<ProductSeed>(seed).ToString());
        }

        return seed;
    }

    public const string NAME_SPACE_USER_PRODUCT_REFRESH_COUNT = "UserProductRefreshCount";

    private int __GetRefreshProductCount(UserProduct.Type type, int group, out string key)
    {
        key = $"{NAME_SPACE_USER_PRODUCT_REFRESH_COUNT}{group}{UserData.SEPARATOR}{type}";

        var result = new Active<int>(PlayerPrefs.GetString(key), __Parse);
        switch (type)
        {
            case UserProduct.Type.Day:
                return result.ToDay();
            case UserProduct.Type.Week:
                return result.ToWeek();
            case UserProduct.Type.Month:
                return result.ToMonth();
        }

        return 0;
    }

    private bool __RefreshProductSeed(UserProduct.Type type, int group)
    {
        int source = __GetRefreshProductCount(type, group, out string key), destination = source + 1;
        
        foreach (var productRefresh in _productRefreshes)
        {
            if(productRefresh.productType != type || productRefresh.group != group || --source >= 0)
                continue;

            if (!__ApplyProductType(productRefresh.name, productRefresh.currencyType, productRefresh.price))
                return false;

            PlayerPrefs.SetString(key, new Active<int>(destination).ToString());
            
            break;
        }
        
        ProductSeed seed;
        seed.value = (uint)UnityEngine.Random.Range(0, int.MaxValue);
        seed.bits = 0;

        PlayerPrefs.SetString(__GetProductTypeKey(type, group), new Active<ProductSeed>(seed).ToString());

        return true;
    }

    private void __CollectProducts(ref List<UserProduct> results, UserProduct.Type type, int group)
    {
        UserProduct userProduct;
        Product product;
        var seed = __GetProductSeed(type, group, out _);
        var random = new Unity.Mathematics.Random(seed.value);
        var randomSelector = new RandomSelector(ref random);
        int numProducts = _products.Length, bitIndex = 0, chapter = UserData.chapter;
        for (int i = 0; i < numProducts; ++i)
        {
            product = _products[i];
            if(product.productType != type || 
               product.group != group ||
               product.minChapter > chapter ||
               product.minChapter < product.maxChapter && product.maxChapter <= chapter || 
               !randomSelector.Select(ref random, product.chance))
                continue;

            userProduct.name = product.name;
            userProduct.id = __ProductBitIndexToID(type, group, bitIndex);
            userProduct.flag = (seed.bits & (1 << bitIndex)) == 0 ? 0 : UserProduct.Flag.Collected;
            userProduct.productType = product.productType;
            userProduct.currencyType = product.currencyType;
            userProduct.group = product.group;
            userProduct.price = product.price;
            userProduct.rewards = product.rewards;
            
            if(results == null)
                results = new List<UserProduct>();
            
            results.Add(userProduct);

            ++bitIndex;
        }

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
    
    public IEnumerator QueryProducts(uint userID, Action<IUserData.Products> onComplete)
    {
        return UserDataMain.instance.QueryProducts(userID, onComplete);
    }

    public IEnumerator RefreshProducts(uint userID, int group, UserProduct.Type type, Action<Memory<UserProduct>> onComplete)
    {
        return UserDataMain.instance.RefreshProducts(userID, group, type, onComplete);
    }

    public IEnumerator BuyProduct(uint userID, uint productID, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.BuyProduct(userID, productID, onComplete);
    }
}
