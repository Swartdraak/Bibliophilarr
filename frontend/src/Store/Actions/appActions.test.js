import { createStore } from 'redux';
import { reducers, saveDimensions, setIsSidebarVisible } from './appActions';

function createDesktopStore() {
  const store = createStore(reducers);
  store.dispatch(saveDimensions({ width: 769, height: 800 }));
  store.dispatch(setIsSidebarVisible({ isSidebarVisible: true }));
  return store;
}

test('closes the navigation drawer on entering the mobile breakpoint', () => {
  const store = createDesktopStore();
  store.dispatch(saveDimensions({ width: 768, height: 800 }));

  expect(store.getState()).toMatchObject({
    dimensions: { isSmallScreen: true, width: 768, height: 800 },
    isSidebarVisible: false
  });
});

test('keeps an explicitly opened drawer visible while resizing within mobile', () => {
  const store = createDesktopStore();
  store.dispatch(saveDimensions({ width: 768, height: 800 }));
  store.dispatch(setIsSidebarVisible({ isSidebarVisible: true }));
  store.dispatch(saveDimensions({ width: 390, height: 700 }));

  expect(store.getState().isSidebarVisible).toBe(true);
});

test('closes a previously opened drawer when returning to the mobile breakpoint', () => {
  const store = createDesktopStore();
  store.dispatch(saveDimensions({ width: 390, height: 700 }));
  store.dispatch(setIsSidebarVisible({ isSidebarVisible: true }));
  store.dispatch(saveDimensions({ width: 1200, height: 800 }));
  store.dispatch(saveDimensions({ width: 390, height: 700 }));

  expect(store.getState().isSidebarVisible).toBe(false);
});
