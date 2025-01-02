﻿using System.Collections.Generic;

namespace ProtonDrive.Shared.Repository;

public interface ICollectionRepository<T>
{
    IReadOnlyCollection<T> GetAll();

    void SetAll(IEnumerable<T> value);
}
