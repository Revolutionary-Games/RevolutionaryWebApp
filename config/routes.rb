Rails.application.routes.draw do
  get 'login/failed', to: "login#failed"
  post 'login', to: "login#local_login"
  delete 'login', to: "login#logout"
  post 'logout', to: "login#logout"
  mount Hyperstack::Engine => '/hyperstack'
  mount API::Base, at: "/"
  # Prevent API leakage when it doesn't exist
  get 'api/', to: proc { [404, {}, ['']] }
  
  get '/(*other)', to: 'hyperstack#app'
  # For details on the DSL available within this file, see http://guides.rubyonrails.org/routing.html
end
