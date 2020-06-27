class LauncherLinkSerializer < ActiveModel::Serializer
  attributes :id, :link_code, :last_ip, :last_connection, :total_api_calls
  has_one :user
end
