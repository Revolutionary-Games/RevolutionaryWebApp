class Home < HyperComponent

  # after_mount do
  #   CurrentUserDetails.run.then do |value|
  #     mutate @user = value
  #   end.fail do |error|
  #     mutate @user = "Error: #{error}"
  #   end    
  # end
  
  render(DIV) do
    H1 { "Hello world from Hyperstack test!" }

    if @user
      H2 { "User: #{@user}" }      
    end
    
  end
end
